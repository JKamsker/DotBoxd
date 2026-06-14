namespace DotBoxD.Plugins;

using DotBoxD.Kernels;
using DotBoxD.Hosting;

public sealed partial class InstalledKernel
{
    private readonly object _typedValueGate = new();
    private readonly object _updateModeGate = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SandboxHost _host;
    private readonly ExecutionPlan _plan;
    private readonly ExecutionMode _executionMode;
    private readonly KernelEntrypoints _entrypoints;
    private readonly LiveStateSyncRegistry _liveStateSync;
    private readonly PluginExecutionObserver _executionObserver = new();
    private readonly PluginEventAdapterValidationCache _adapterValidation = new();
    private readonly Dictionary<Type, object> _typedValues = [];
    private readonly Dictionary<Type, LiveUpdateMode> _updateModes = [];
    private readonly PendingLiveUpdateQueue _pendingLiveUpdates = new();
    private readonly CancellationTokenSource _revocation = new();
    private readonly object? _ownerId;
    private readonly SandboxExecutionOptions _executionOptions;
    private int _revoked;

    internal InstalledKernel(
        SandboxHost host,
        ExecutionPlan plan,
        PluginPackage package,
        ExecutionMode executionMode,
        object? ownerId = null)
    {
        _host = host;
        _plan = plan;
        _executionMode = executionMode;
        _ownerId = ownerId;
        Package = package;
        Manifest = package.Manifest;
        Value = LiveSettingStore.FromDefinitions(Manifest.LiveSettings);
        _entrypoints = package.Entrypoints;
        _liveStateSync = new LiveStateSyncRegistry(GetUpdateMode);
        _executionOptions = new SandboxExecutionOptions { Mode = executionMode, SuppressSuccessfulRunSummaryAudit = true };
    }

    public PluginPackage Package { get; }
    public PluginManifest Manifest { get; }
    public LiveSettingStore Value { get; }
    public Exception? LastAsyncUpdateError => _pendingLiveUpdates.LastError;
    public PluginExecutionObservation? LastExecution => _executionObserver.Last;
    public IReadOnlyList<PluginExecutionObservation> ExecutionObservations => _executionObserver.Snapshot();
    public bool IsRevoked => Volatile.Read(ref _revoked) != 0;

    /// <summary>
    /// Opaque owner token of the session that installed this kernel, or <c>null</c> for kernels
    /// installed directly on the server (no session). Used by <see cref="KernelRegistry"/> to reject
    /// cross-owner id reuse so one plugin cannot replace another plugin's kernel.
    /// </summary>
    public object? OwnerId => _ownerId;

    public void Revoke()
    {
        if (Interlocked.Exchange(ref _revoked, 1) == 0)
        {
            _revocation.Cancel();
        }
    }

    internal void RegisterStateSynchronizer(Type stateType, Action synchronize)
        => _liveStateSync.Register(stateType, synchronize);

    internal TSettings GetTypedValue<TSettings>() where TSettings : class
    {
        if (typeof(TSettings).IsInterface)
        {
            return Value.As<TSettings>();
        }

        lock (_typedValueGate)
        {
            if (_typedValues.TryGetValue(typeof(TSettings), out var value))
            {
                return (TSettings)value;
            }

            var created = LiveKernelValueFactory.Create<TSettings>(this);
            _typedValues[typeof(TSettings)] = created;
            return created;
        }
    }

    internal LiveUpdateMode GetUpdateMode(Type stateType)
    {
        lock (_updateModeGate)
        {
            return _updateModes.TryGetValue(stateType, out var mode) ? mode : LiveUpdateMode.Sync;
        }
    }

    internal void SetUpdateMode(Type stateType, LiveUpdateMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Live update mode is not supported.");
        }

        lock (_updateModeGate)
        {
            _updateModes[stateType] = mode;
        }
    }

    public async ValueTask FlushUpdatesAsync(CancellationToken cancellationToken = default)
    {
        _liveStateSync.SynchronizeForFlush();
        _pendingLiveUpdates.ClearError();
        await _pendingLiveUpdates.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ShouldHandleAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            ValidateFor(adapter);
            var input = BuildInput(adapter, e);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            return AsShouldHandleResult(result);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public async ValueTask HandleAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            ValidateFor(adapter);
            var input = BuildInput(adapter, e);
            _ = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    internal async ValueTask InvokeAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SandboxRuntimeException) when (IsRevoked)
        {
            // Revoked while queued on the execution gate: skip silently rather than fault the publish.
            return;
        }

        try
        {
            if (IsRevoked)
            {
                return;
            }

            ValidateFor(adapter);
            var input = BuildInput(adapter, e);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            if (AsShouldHandleResult(result))
            {
                if (IsRevoked)
                {
                    return;
                }

                _ = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    // Acquires the per-kernel execution gate so that Revoke() unblocks any queued waiter. The
    // uncontended path takes the semaphore synchronously (no allocation, preserving the hot path);
    // only when it must actually wait does it link the caller's token with the revocation token so a
    // concurrent Revoke() cancels the wait. A wait cancelled by revocation surfaces as the standard
    // "capability was revoked" SandboxRuntimeException; a wait cancelled by the caller surfaces as
    // OperationCanceledException.
    private async ValueTask AcquireExecutionGateAsync(CancellationToken cancellationToken)
    {
        PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
        if (_executionGate.Wait(0, CancellationToken.None))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _revocation.Token);
        try
        {
            await _executionGate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsRevoked && !cancellationToken.IsCancellationRequested)
        {
            PluginKernelRevocation.ThrowIfRevoked(true);
            throw;
        }

        PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
    }

    private static bool AsShouldHandleResult(SandboxValue result)
    {
        if (result is not BoolValue handled)
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "kernel ShouldHandle returned a non-bool value"));
        }

        return handled.Value;
    }

    internal void ValidateFor<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => _adapterValidation.Validate(Manifest, _plan, _entrypoints, adapter);

    private SandboxValue BuildInput<TEvent>(IPluginEventAdapter<TEvent> adapter, TEvent e)
        => PluginKernelInputBuilder.Build(
            adapter,
            e,
            _liveStateSync.SynchronizeForInput(),
            Manifest.LiveSettings,
            Value,
            _pendingLiveUpdates.Enqueue);

    private void RefreshTypedValuesFromStore()
    {
        lock (_typedValueGate)
        {
            foreach (var value in _typedValues.Values)
            {
                var type = value.GetType();
                if (type.IsInterface)
                {
                    continue;
                }

                LiveKernelValueFactory.PullFromStore(this, value);
            }
        }
    }

}
