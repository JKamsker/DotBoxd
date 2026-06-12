namespace SafeIR.Plugins;

using SafeIR;
using SafeIR.Hosting;

public sealed class InstalledKernel
{
    private readonly object _typedValueGate = new();
    private readonly object _updateModeGate = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SandboxHost _host;
    private readonly ExecutionPlan _plan;
    private readonly KernelEntrypoints _entrypoints;
    private readonly List<LiveStateSynchronizer> _stateSynchronizers = [];
    private readonly Dictionary<Type, object> _typedValues = [];
    private readonly Dictionary<Type, LiveUpdateMode> _updateModes = [];
    private readonly PendingLiveUpdateQueue _pendingLiveUpdates = new();
    private int _revoked;

    internal InstalledKernel(SandboxHost host, ExecutionPlan plan, PluginPackage package)
    {
        _host = host;
        _plan = plan;
        Package = package;
        Manifest = package.Manifest;
        Value = LiveSettingStore.FromDefinitions(Manifest.LiveSettings);
        _entrypoints = package.Entrypoints;
    }

    public PluginPackage Package { get; }
    public PluginManifest Manifest { get; }
    public LiveSettingStore Value { get; }
    public Exception? LastAsyncUpdateError => _pendingLiveUpdates.LastError;
    public bool IsRevoked => Volatile.Read(ref _revoked) != 0;

    public void Revoke() => Volatile.Write(ref _revoked, 1);

    internal void RegisterStateSynchronizer(Type stateType, Action synchronize)
        => _stateSynchronizers.Add(new LiveStateSynchronizer(stateType, synchronize));

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
        lock (_updateModeGate)
        {
            _updateModes[stateType] = mode;
        }
    }

    public ValueTask FlushUpdatesAsync(CancellationToken cancellationToken = default)
        => _pendingLiveUpdates.FlushAsync(cancellationToken);

    public async ValueTask<bool> ShouldHandleAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            var input = BuildInput(adapter, e);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
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
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            var input = BuildInput(adapter, e);
            _ = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
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
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRevoked)
            {
                return;
            }

            var input = BuildInput(adapter, e);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            if (AsShouldHandleResult(result))
            {
                _ = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public async ValueTask ModifySettingsAsync(
        IReadOnlyDictionary<string, object?> values,
        bool atomic = false,
        CancellationToken cancellationToken = default)
    {
        if (atomic)
        {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            Value.SetMany(values);
            RefreshTypedValuesFromStore();
        }
        finally
        {
            if (atomic)
            {
                _executionGate.Release();
            }
        }
    }

    internal async ValueTask ModifyAsync<TState>(
        TState current,
        Action<TState> modify,
        bool atomic,
        CancellationToken cancellationToken) where TState : class
    {
        ArgumentNullException.ThrowIfNull(modify);
        if (atomic)
        {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            if (typeof(TState).IsInterface)
            {
                var draftStore = Value.Copy(Manifest.LiveSettings);
                var draft = draftStore.As<TState>();
                modify(draft);
                Value.SetMany(draftStore.ToObjectValues(Manifest.LiveSettings));
                RefreshTypedValuesFromStore();
                return;
            }

            var classDraft = LiveKernelValueFactory.CreateDraft(current);
            modify(classDraft);
            Value.SetMany(LiveKernelValueFactory.ExtractSettings(classDraft, Manifest.LiveSettings));
            LiveKernelValueFactory.CopyLiveProperties(classDraft, current);
            RefreshTypedValuesFromStore();
        }
        finally
        {
            if (atomic)
            {
                _executionGate.Release();
            }
        }
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
        => KernelEntrypointValidator.Validate(Manifest, _plan, _entrypoints, adapter);

    private async ValueTask<SandboxValue> ExecutePreparedAsync(
        string entrypoint,
        SandboxValue input,
        CancellationToken cancellationToken)
    {
        var result = await _host.ExecuteAsync(
                _plan,
                entrypoint,
                input,
                new SandboxExecutionOptions { Mode = Manifest.Mode },
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new SandboxRuntimeException(result.Error ?? new SandboxError(SandboxErrorCode.HostFailure, "kernel execution failed"));
        }

        return result.Value ?? SandboxValue.Unit;
    }

    private SandboxValue BuildInput<TEvent>(IPluginEventAdapter<TEvent> adapter, TEvent e)
    {
        var deferredUpdates = SynchronizeLiveStateForInput();
        var values = adapter.ToSandboxValues(e)
            .Concat(Value.ToSandboxValues(Manifest.LiveSettings))
            .ToArray();
        foreach (var update in deferredUpdates)
        {
            _pendingLiveUpdates.Enqueue(update);
        }

        return SandboxValue.FromList(values);
    }

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

    private List<Action> SynchronizeLiveStateForInput()
    {
        var deferredUpdates = new List<Action>();
        foreach (var synchronize in _stateSynchronizers)
        {
            var mode = GetUpdateMode(synchronize.StateType);
            if ((mode & LiveUpdateMode.AsyncSet) == LiveUpdateMode.AsyncSet)
            {
                deferredUpdates.Add(synchronize.Synchronize);
                continue;
            }

            synchronize.Synchronize();
        }

        return deferredUpdates;
    }

    private sealed record LiveStateSynchronizer(Type StateType, Action Synchronize);
}
