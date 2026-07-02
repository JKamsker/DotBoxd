using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer.Inbound;

internal static class RpcPeerInboundRequestReader
{
    public static bool TryRead(
        RpcFrame frame,
        ISerializer serializer,
        int expectedMessageId,
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
            if (expectedMessageId == 0 || request.MessageId == 0)
            {
                protocolError = "Request message id must not be zero.";
                return false;
            }

            if (request.MessageId != expectedMessageId)
            {
                protocolError = "Request envelope message id does not match frame header.";
                return false;
            }

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
