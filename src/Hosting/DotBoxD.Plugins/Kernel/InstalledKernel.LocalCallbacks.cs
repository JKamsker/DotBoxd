using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Kernel;

public sealed partial class InstalledKernel
{
    public async ValueTask<SandboxValue?> TryEvaluateHandleAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        CancellationToken cancellationToken = default)
    {
        await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            ValidateLocalCallbackFor(adapter);
            var input = BuildInput(adapter, e, _entrypoints.ShouldHandle);
            var result = await ExecutePreparedAsync(_entrypoints.ShouldHandle, input, cancellationToken).ConfigureAwait(false);
            if (!AsShouldHandleResult(result))
            {
                PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
                return null;
            }

            if (UsesReusableNoAuditInput(_entrypoints.ShouldHandle) &&
                !UsesReusableNoAuditInput(_entrypoints.Handle))
            {
                input = SnapshotInput(input);
            }

            var payload = await ExecutePreparedAsync(_entrypoints.Handle, input, cancellationToken).ConfigureAwait(false);
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            return payload;
        }
        finally
        {
            _executionGate.Release();
        }
    }

    internal void ValidateLocalCallbackFor<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => _adapterValidation.ValidateLocalCallback(Manifest, _plan, _entrypoints, adapter);
}
