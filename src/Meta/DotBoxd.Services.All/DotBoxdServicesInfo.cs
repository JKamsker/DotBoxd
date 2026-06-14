namespace DotBoxd.Services.All;

/// <summary>
/// Marker type for the <c>DotBoxd.Services.All</c> meta-package. The package carries no logic; it
/// bundles the service-only stack — the source-generated RPC core (<c>DotBoxd.Services</c>), the
/// MessagePack codec (<c>DotBoxd.Codecs.MessagePack</c>), and the TCP and named-pipe transports
/// (<c>DotBoxd.Transports.Tcp</c>, <c>DotBoxd.Transports.NamedPipes</c>). It targets
/// <c>netstandard2.1</c> and is Unity/IL2CPP compatible.
/// </summary>
public static class DotBoxdServicesInfo
{
    /// <summary>The components bundled by this service/channels meta-package.</summary>
    public const string Components =
        "DotBoxd.Services, DotBoxd.Codecs.MessagePack, DotBoxd.Transports.Tcp, DotBoxd.Transports.NamedPipes";
}
