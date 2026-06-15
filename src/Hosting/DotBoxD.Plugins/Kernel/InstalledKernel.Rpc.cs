using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Kernel;

/// <summary>
/// Request/response invocation for a <b>server extension</b> kernel: the path that runs a verified
/// batch entrypoint server-side in one call and returns its result. Kept in a partial alongside the
/// event <c>ShouldHandle</c>/<c>Handle</c> path in <see cref="InstalledKernel"/>.
/// </summary>
public sealed partial class InstalledKernel
{
    /// <summary>
    /// Invokes the kernel's server-extension entrypoint request/response: the caller arguments are bound to the
    /// entrypoint's leading parameters (live settings fill the trailing ones), the verified IR runs once
    /// under the execution gate, and its result value is returned. Unlike <see cref="HandleAsync"/> the
    /// result is not discarded. The kernel must have been installed via
    /// <see cref="PluginServer.InstallServerExtensionAsync"/> (its manifest declares the rpcEntrypoint).
    /// </summary>
    public async ValueTask<SandboxValue> InvokeServerExtensionAsync(
        IReadOnlyList<SandboxValue> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (Manifest.RpcEntrypoint is not { } entrypoint)
        {
            throw new InvalidOperationException(
                $"Kernel '{Manifest.PluginId}' is not a server extension (no rpcEntrypoint).");
        }

        await AcquireExecutionGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            var input = BuildRpcInput(entrypoint, arguments);
            var result = await ExecutePreparedAsync(entrypoint, input, cancellationToken).ConfigureAwait(false);
            PluginKernelRevocation.ThrowIfRevoked(IsRevoked);
            return result;
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private SandboxValue BuildRpcInput(string entrypoint, IReadOnlyList<SandboxValue> arguments)
    {
        var function = FindRpcEntrypoint(entrypoint);
        var liveSettings = Manifest.LiveSettings;
        var callerCount = function.Parameters.Count - liveSettings.Count;
        if (callerCount < 0)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError, "server extension entrypoint declares fewer parameters than live settings"));
        }

        if (arguments.Count != callerCount)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                $"server extension entrypoint '{entrypoint}' expects {callerCount} argument(s) but received {arguments.Count}"));
        }

        if (function.Parameters.Count == 0)
        {
            return SandboxValue.Unit;
        }

        if (function.Parameters.Count == 1)
        {
            return callerCount == 1 ? arguments[0] : Value.ToSandboxValue(liveSettings[0]);
        }

        var values = new SandboxValue[function.Parameters.Count];
        for (var i = 0; i < callerCount; i++)
        {
            values[i] = arguments[i];
        }

        if (liveSettings.Count > 0)
        {
            Value.CopySandboxValues(liveSettings, values, callerCount);
        }

        return SandboxValue.FromList(values, values[0].Type);
    }

    private SandboxFunction FindRpcEntrypoint(string entrypoint)
    {
        foreach (var function in Package.Module.Functions)
        {
            if (function.IsEntrypoint && string.Equals(function.Id, entrypoint, StringComparison.Ordinal))
            {
                return function;
            }
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.ValidationError, $"server extension entrypoint '{entrypoint}' was not found"));
    }
}
