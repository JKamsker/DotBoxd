using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal static class RpcPeerInboundRequestReader
{
    public static bool TryRead(
        RpcFrame frame,
        ISerializer serializer,
        out RpcRequest request,
        out ReadOnlyMemory<byte> payload,
        out string? protocolError,
        out Exception? error)
    {
        request = default;
        payload = ReadOnlyMemory<byte>.Empty;
        protocolError = null;
        error = null;

        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out _, out var envelope, out payload))
        {
            protocolError = "Malformed request frame.";
            return false;
        }

        try
        {
            request = serializer.Deserialize<RpcRequest>(envelope);
            return true;
        }
        catch (Exception ex)
        {
            protocolError = "Malformed request envelope.";
            error = ex;
            return false;
        }
    }
}
