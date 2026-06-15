namespace DotBoxD.Plugins;

/// <summary>
/// Client-side transport used by generated server extension proxies. The payload is DotBoxD's compact
/// server extension value IR encoded by <see cref="KernelRpcBinaryCodec"/>, so transports can carry it as an
/// ordinary binary IPC argument without knowing the plugin-owned service contract.
/// </summary>
public interface IServerExtensionWireClient
{
    ValueTask<byte[]> InvokeServerExtensionAsync(
        string pluginId,
        byte[] arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adds service-contract lookup for generated domain-style server extension client extensions.
/// </summary>
public interface IServerExtensionClientRegistry : IServerExtensionWireClient
{
    string PluginId<TService>()
        where TService : class;
}
