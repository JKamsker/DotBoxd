using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerReadLoop
{
    private readonly IRpcChannel _channel;
    private readonly IRpcValueTaskChannel? _valueTaskChannel;
    private readonly IRpcFrameChannel? _frameChannel;
    private readonly RpcPeerInboundDispatcher _inbound;
    private readonly RpcPeerOutboundInvoker _outbound;
    private readonly RpcStreamManager _streams;
    private readonly RpcPeerFrameProcessor _frameProcessor;
    private readonly Action _markClosed;
    private readonly Action<Exception> _readError;
    private readonly Action<Exception?> _disconnected;

    public RpcPeerReadLoop(
        IRpcChannel channel,
        RpcPeerInboundDispatcher inbound,
        RpcPeerOutboundInvoker outbound,
        RpcStreamManager streams,
        RpcPeerFrameProcessor frameProcessor,
        Action markClosed,
        Action<Exception> readError,
        Action<Exception?> disconnected)
    {
        _channel = channel;
        _valueTaskChannel = channel as IRpcValueTaskChannel;
        _frameChannel = channel as IRpcFrameChannel;
        _inbound = inbound;
        _outbound = outbound;
        _streams = streams;
        _frameProcessor = frameProcessor;
        _markClosed = markClosed;
        _readError = readError;
        _disconnected = disconnected;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Exception? readError = null;
        try
        {
            await ReadFramesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            readError = ex;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await StopAfterRemoteCloseAsync(readError).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _channel.IsConnected)
        {
            RpcFrame frame;
            try
            {
                if (_frameChannel is not null)
                {
                    frame = await _frameChannel.ReceiveFrameValueAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    var payload = _valueTaskChannel is null
                        ? await _channel.ReceiveAsync(ct).ConfigureAwait(false)
                        : await _valueTaskChannel.ReceiveValueAsync(ct).ConfigureAwait(false);
                    frame = new RpcFrame(payload);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            if (frame.Length == 0)
            {
                frame.Dispose();
                break;
            }

            var disposeFrame = true;
            try
            {
                disposeFrame = await _frameProcessor.ShouldDisposeAsync(frame, ct).ConfigureAwait(false);
            }
            finally
            {
                if (disposeFrame)
                {
                    frame.Dispose();
                }
            }
        }
    }

    private async Task StopAfterRemoteCloseAsync(Exception? readError)
    {
        _markClosed();
        _outbound.FailPending(
            readError is null
                ? new ShaRpcConnectionException("Connection closed.")
                : new ShaRpcConnectionException("Connection lost.", readError));
        _streams.Stop();
        await _inbound.StopAsync().ConfigureAwait(false);

        if (readError is not null)
        {
            _readError(readError);
        }

        _disconnected(readError);
    }
}
