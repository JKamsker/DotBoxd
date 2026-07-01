using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer;

internal sealed class RpcPeerFrameProcessor
{
    private readonly RpcPeerInboundDispatcher _inbound;
    private readonly RpcPeerOutboundInvoker _outbound;
    private readonly RpcStreamManager _streams;
    private readonly Action<int, MessageType, string, Exception?> _protocolError;

    public RpcPeerFrameProcessor(
        RpcPeerInboundDispatcher inbound,
        RpcPeerOutboundInvoker outbound,
        RpcStreamManager streams,
        Action<int, MessageType, string, Exception?> protocolError)
    {
        _inbound = inbound;
        _outbound = outbound;
        _streams = streams;
        _protocolError = protocolError;
    }

    public async ValueTask<bool> ShouldDisposeAsync(RpcFrame frame, CancellationToken ct)
    {
        if (!MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out var messageType))
        {
            _protocolError(0, default, "Malformed frame header.", null);
            return true;
        }

        switch (messageType)
        {
            case MessageType.Response:
            case MessageType.Error:
                return !_outbound.TryCompleteResponse(messageId, frame);
            case MessageType.Request:
                return !await _inbound.AcceptRequestAsync(frame, messageId, ct).ConfigureAwait(false);
            case MessageType.Cancel:
                if (messageId <= 0 || frame.Memory.Length != MessageFramer.HeaderSize)
                {
                    _protocolError(messageId, messageType, "Malformed cancel frame.", null);
                    return true;
                }

                _inbound.Cancel(messageId);
                return true;
            case MessageType.StreamCancel:
                _streams.CancelOutbound(messageId);
                return true;
            case MessageType.StreamItem:
                var itemFrame = frame.DetachPayload();
                if (_streams.TryAcceptItem(messageId, itemFrame))
                {
                    return false;
                }

                itemFrame.Dispose();
                _protocolError(messageId, messageType, "Unknown stream id.", null);
                return true;
            case MessageType.StreamComplete:
                if (!RpcStreamCompleteFrameReader.TryRead(frame.Memory, out var streamId))
                {
                    _protocolError(messageId, messageType, "Malformed stream complete frame.", null);
                    return true;
                }

                if (!_streams.TryCompleteInbound(streamId))
                {
                    _protocolError(messageId, messageType, "Unknown stream id.", null);
                }

                return true;
            case MessageType.StreamError:
                if (!_streams.TryCompleteInboundError(frame.Memory, out var malformed))
                {
                    var message = malformed ? "Malformed stream error frame." : "Unknown stream id.";
                    _protocolError(messageId, messageType, message, null);
                }

                return true;
            case MessageType.StreamCredit:
                if (!_streams.TryAddCredit(frame.Memory))
                {
                    _protocolError(messageId, messageType, "Malformed stream credit frame.", null);
                }

                return true;
            default:
                _protocolError(messageId, messageType, "Unknown message type.", null);
                return true;
        }
    }

    public ValueTask<bool> ShouldDisposeAsync(Payload frame, CancellationToken ct) =>
        ShouldDisposeAsync(new RpcFrame(frame), ct);
}
