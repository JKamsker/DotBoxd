namespace ShaRPC.Core.Protocol;

/// <summary>
/// Describes the wire format carried by a ShaRPC stream.
/// </summary>
public enum RpcStreamKind : byte
{
    /// <summary>Raw byte chunks, used for <see cref="System.IO.Stream"/> and pipes.</summary>
    Binary = 1,

    /// <summary>Serializer-framed items, used for <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/>.</summary>
    Items = 2,
}
