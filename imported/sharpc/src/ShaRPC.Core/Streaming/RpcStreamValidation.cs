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
        if (handles is null || handles.Length == 0)
        {
            return true;
        }

        if (handles.Length == 1)
        {
            return TryValidateInboundHandle(handles[0], out protocolError);
        }

        var streamIds = new HashSet<int>(handles.Length);
        foreach (var handle in handles)
        {
            if (!TryValidateInboundHandle(handle, out protocolError))
            {
                return false;
            }

            if (!streamIds.Add(handle.StreamId))
            {
                protocolError = $"Duplicate inbound stream id '{handle.StreamId}'.";
                return false;
            }
        }

        return true;
    }

    public static void ValidateOutboundAttachments(RpcStreamAttachment[] attachments)
    {
        if (attachments.Length == 1)
        {
            ValidateOutboundAttachment(attachments[0]);
            return;
        }

        var streamIds = new HashSet<int>(attachments.Length);
        foreach (var attachment in attachments)
        {
            ValidateOutboundAttachment(attachment);
            if (!streamIds.Add(attachment.Handle.StreamId))
            {
                throw new ShaRpcProtocolException($"Duplicate outbound stream id '{attachment.Handle.StreamId}'.");
            }
        }
    }

    public static void ValidateOutboundAttachment(RpcStreamAttachment attachment)
    {
        if (attachment is null)
        {
            throw new ArgumentNullException(nameof(attachment), "Outbound stream attachment must not be null.");
        }

        if (attachment.Handle.StreamId == 0)
        {
            throw new ShaRpcProtocolException("Stream id must not be zero.");
        }

        ValidateKind(attachment.Handle.Kind);
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

    private static bool TryValidateInboundHandle(
        RpcStreamHandle handle,
        out string? protocolError)
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

        protocolError = null;
        return true;
    }
}
