namespace ShaRPC.Core.Protocol;

/// <summary>
/// Identifies a stream multiplexed over the current ShaRPC connection.
/// </summary>
public struct RpcStreamHandle
{
    public RpcStreamHandle(int streamId, RpcStreamKind kind)
    {
        StreamId = streamId;
        Kind = kind;
    }

    /// <summary>The frame message id used by stream item, completion, error, and credit frames.</summary>
    public int StreamId { get; set; }

    /// <summary>The payload shape carried by the stream.</summary>
    public RpcStreamKind Kind { get; set; }
}
