namespace SafeIR.Plugins;

using SafeIR;
using SafeIR.Hosting;

public sealed class InstalledKernel
{
    private readonly object _typedValueGate = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SandboxHost _host;
    private readonly ExecutionPlan _plan;
    private readonly KernelEntrypoints _entrypoints;
    private readonly List<Action> _stateSynchronizers = [];
    private readonly Dictionary<Type, object> _typedValues = [];

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

    internal void RegisterStateSynchronizer(Action synchronize)
        => _stateSynchronizers.Add(synchronize);

    internal TSettings GetTypedValue<TSettings>() where TSettings : class
    {
        if (typeof(TSettings).IsInterface) {
            return Value.As<TSettings>();
        }

        lock (_typedValueGate) {
            if (_typedValues.TryGetValue(typeof(TSettings), out var value)) {
                return (TSettings)value;
            }

            var created = LiveKernelValueFactory.Create<TSettings>(this);
            _typedValues[typeof(TSettings)] = created;
            return created;
        }
    }

    public async ValueTask<bool> ShouldHandleAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var input = BuildInput(adapter, e);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            return AsShouldHandleResult(result);
        }
        finally {
            _executionGate.Release();
        }
    }

    public async ValueTask HandleAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var input = BuildInput(adapter, e);
            _ = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _executionGate.Release();
        }
    }

    internal async ValueTask InvokeAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var input = BuildInput(adapter, e);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            if (AsShouldHandleResult(result)) {
                _ = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            _executionGate.Release();
        }
    }

    public async ValueTask ModifySettingsAsync(
        IReadOnlyDictionary<string, object?> values,
        bool atomic = false,
        CancellationToken cancellationToken = default)
    {
        if (atomic) {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try {
            Value.SetMany(values);
            RefreshTypedValuesFromStore();
        }
        finally {
            if (atomic) {
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
        if (atomic) {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try {
            if (typeof(TState).IsInterface) {
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
        finally {
            if (atomic) {
                _executionGate.Release();
            }
        }
    }

    private static bool AsShouldHandleResult(SandboxValue result)
    {
        if (result is not BoolValue handled) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "kernel ShouldHandle returned a non-bool value"));
        }

        return handled.Value;
    }

    internal void ValidateFor<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        if (!Manifest.Subscriptions.Any(s => string.Equals(s.Event, adapter.EventName, StringComparison.Ordinal))) {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP031", $"Plugin '{Manifest.PluginId}' is not subscribed to event '{adapter.EventName}'.")
            ]);
        }

        var expected = adapter.Parameters
            .Concat(Manifest.LiveSettings.Select(s => new Parameter(s.Name, LiveSettingTypeConverter.ToSandboxType(s.Type))))
            .ToArray();
        ValidateFunction(_entrypoints.ShouldHandle, SandboxType.Bool, expected);
        ValidateFunction(_entrypoints.Handle, SandboxType.Unit, expected);
    }

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
        if (!result.Succeeded) {
            throw new SandboxRuntimeException(result.Error ?? new SandboxError(SandboxErrorCode.HostFailure, "kernel execution failed"));
        }

        return result.Value ?? SandboxValue.Unit;
    }

    private SandboxValue BuildInput<TEvent>(IPluginEventAdapter<TEvent> adapter, TEvent e)
    {
        SynchronizeLiveState();
        var values = adapter.ToSandboxValues(e)
            .Concat(Value.ToSandboxValues(Manifest.LiveSettings))
            .ToArray();
        return SandboxValue.FromList(values);
    }

    private void RefreshTypedValuesFromStore()
    {
        lock (_typedValueGate) {
            foreach (var value in _typedValues.Values) {
                var type = value.GetType();
                if (type.IsInterface) {
                    continue;
                }

                LiveKernelValueFactory.PullFromStore(this, value);
            }
        }
    }

    private void SynchronizeLiveState()
    {
        foreach (var synchronize in _stateSynchronizers) {
            synchronize();
        }
    }

    private void ValidateFunction(string functionId, SandboxType returnType, IReadOnlyList<Parameter> expected)
    {
        var function = _plan.Module.Functions.FirstOrDefault(f => string.Equals(f.Id, functionId, StringComparison.Ordinal));
        if (function is null || !function.IsEntrypoint) {
            throw new SandboxValidationException([
                new SandboxDiagnostic("SGP032", $"Kernel entrypoint '{functionId}' is missing or not public.")
            ]);
        }

        if (function.ReturnType != returnType || function.Parameters.Count != expected.Count) {
            throw SignatureError(functionId);
        }

        for (var i = 0; i < expected.Count; i++) {
            if (function.Parameters[i].Type != expected[i].Type) {
                throw SignatureError(functionId);
            }
        }
    }

    private SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("SGP033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}

public sealed class TypedInstalledKernel<TSettings> where TSettings : class
{
    internal TypedInstalledKernel(InstalledKernel kernel)
    {
        Kernel = kernel;
        Value = kernel.GetTypedValue<TSettings>();
    }

    public InstalledKernel Kernel { get; }
    public TSettings Value { get; }

    public ValueTask ModifyAsync(
        Action<TSettings> modify,
        bool atomic = false,
        CancellationToken cancellationToken = default)
        => Kernel.ModifyAsync(Value, modify, atomic, cancellationToken);
}
