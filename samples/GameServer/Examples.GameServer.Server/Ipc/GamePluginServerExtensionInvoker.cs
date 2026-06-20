using DotBoxD.Plugins.Kernel;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal sealed class GamePluginServerExtensionInvoker
{
    private readonly PluginServer _server;
    private readonly PluginSession _session;

    public GamePluginServerExtensionInvoker(PluginServer server, PluginSession session)
    {
        _server = server;
        _session = session;
    }

    public async ValueTask<byte[]> InvokeAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!_server.Kernels.TryGet(pluginId, out var kernel) ||
            !ReferenceEquals(kernel.OwnerId, _session))
        {
            throw new InvalidOperationException($"Server extension '{pluginId}' is not owned by this plugin session.");
        }

        var function = RpcEntrypoint(kernel);
        var rpcArguments = KernelRpcBinaryCodec.DecodeArguments(arguments);
        var liveSettings = kernel.Manifest.LiveSettings.Count;
        var callerCount = function.Parameters.Count - liveSettings;
        if (callerCount < 0 || rpcArguments.Length != callerCount)
        {
            throw new InvalidOperationException(
                $"Server extension '{pluginId}' expects {callerCount} argument(s) but received {rpcArguments.Length}.");
        }

        var sandboxArguments = new SandboxValue[rpcArguments.Length];
        for (var i = 0; i < rpcArguments.Length; i++)
        {
            sandboxArguments[i] = KernelRpcValueConverter.ToSandboxValue(rpcArguments[i], function.Parameters[i].Type);
        }

        var result = await kernel.InvokeServerExtensionAsync(sandboxArguments, ct).ConfigureAwait(false);
        return KernelRpcBinaryCodec.EncodeValue(KernelRpcValueConverter.FromSandboxValue(result));
    }

    private static SandboxFunction RpcEntrypoint(InstalledKernel kernel)
    {
        if (kernel.Manifest.RpcEntrypoint is not { } entrypoint)
        {
            throw new InvalidOperationException($"Kernel '{kernel.Manifest.PluginId}' is not a server extension.");
        }

        foreach (var function in kernel.Package.Module.Functions)
        {
            if (function.IsEntrypoint && string.Equals(function.Id, entrypoint, StringComparison.Ordinal))
            {
                return function;
            }
        }

        throw new InvalidOperationException(
            $"Server extension '{kernel.Manifest.PluginId}' is missing entrypoint '{entrypoint}'.");
    }
}
