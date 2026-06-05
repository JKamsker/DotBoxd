using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Streaming;

internal static class RpcStreamValidation
{
    public static bool TryValidateInboundHandles(
        RpcStreamHandle[]? handles,
        out string? protocolError)
    {
        protocolError = null;
        if (handles is null)
        {
            return true;
        }

        foreach (var handle in handles)
        {
            if (handle.StreamId == 0)
            {
                protocolError = "Stream id must not be zero.";
                return false;
            }

            if (!IsKnownKind(handle.Kind))
            {
                protocolError = $"Unknown stream kind '{handle.Kind}'.";
                return false;
            }
        }

        return true;
    }

    public static void ValidateOutboundAttachments(RpcStreamAttachment[] attachments)
    {
        var streamIds = new HashSet<int>();
        foreach (var attachment in attachments)
        {
            if (attachment is null)
            {
                throw new ArgumentNullException(nameof(attachments), "Outbound stream attachment must not be null.");
            }

            if (attachment.Handle.StreamId == 0)
            {
                throw new ShaRpcProtocolException("Stream id must not be zero.");
            }

            ValidateKind(attachment.Handle.Kind);
            if (!streamIds.Add(attachment.Handle.StreamId))
            {
                throw new ShaRpcProtocolException($"Duplicate outbound stream id '{attachment.Handle.StreamId}'.");
            }
        }
    }

    public static void ValidateKind(RpcStreamKind kind)
    {
        if (!IsKnownKind(kind))
        {
            throw new ShaRpcProtocolException($"Unknown stream kind '{kind}'.");
        }
    }

    private static bool IsKnownKind(RpcStreamKind kind) =>
        kind == RpcStreamKind.Binary || kind == RpcStreamKind.Items;
}
