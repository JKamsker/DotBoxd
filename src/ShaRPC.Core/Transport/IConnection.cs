namespace ShaRPC.Core.Transport;

/// <summary>
/// Represents a bidirectional connection for sending and receiving data. This is the
/// legacy spelling of <see cref="IRpcChannel"/> and adds no members of its own — every
/// connection is already a channel, so it can back an <see cref="ShaRPC.Core.RpcPeer"/>.
/// </summary>
/// <remarks>
/// Prefer <see cref="IRpcChannel"/> in new code. <see cref="IConnection"/> is retained because the
/// transport contracts (<see cref="ITransport.Connection"/>, <see cref="IServerTransport.AcceptAsync"/>)
/// still return it; it is not marked <c>[Obsolete]</c> until that surface is migrated, to avoid
/// flooding every transport implementer with unsuppressable CS0618 warnings.
/// </remarks>
public interface IConnection : IRpcChannel
{
}
