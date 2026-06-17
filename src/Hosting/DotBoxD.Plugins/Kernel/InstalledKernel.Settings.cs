using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Kernel;

/// <summary>
/// Live-settings mutation for <see cref="InstalledKernel"/>. Split out to keep the execution core
/// small; shares the partial class's fields and the execution gate so atomic updates serialize
/// against kernel runs.
/// </summary>
public sealed partial class InstalledKernel
{
    public async ValueTask ModifySettingsAsync(
        IReadOnlyDictionary<string, object?> values,
        bool atomic = false,
        CancellationToken cancellationToken = default)
    {
        if (atomic)
        {
            await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            CommitSettings(values);
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
            await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            if (typeof(TState).IsInterface)
            {
                var draftStore = Value.Copy(Manifest.LiveSettings);
                var draft = draftStore.As<TState>();
                modify(draft);
                CommitSettings(draftStore.ToObjectValues(Manifest.LiveSettings));
                return;
            }

            var classDraft = LiveKernelValueFactory.CreateDraft(current);
            modify(classDraft);
            lock (_lifecycleGate)
            {
                PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
                Value.SetMany(LiveKernelValueFactory.ExtractSettings(classDraft, Manifest.LiveSettings));
                LiveKernelValueFactory.CopyLiveProperties(classDraft, current);
                RefreshTypedValuesFromStore();
            }
        }
        finally
        {
            if (atomic)
            {
                _executionGate.Release();
            }
        }
    }

    private void CommitSettings(IReadOnlyDictionary<string, object?> values)
    {
        lock (_lifecycleGate)
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            Value.SetMany(values);
            RefreshTypedValuesFromStore();
        }
    }

    internal void CommitSynchronizedLiveValues(IReadOnlyDictionary<string, object?> values)
        => CommitSettings(values);
}
