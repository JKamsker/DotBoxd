using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal sealed class RpcDispatchResponseBuilder
{
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers;
    private readonly Func<Exception, RpcErrorInfo?>? _exceptionTransformer;

    public RpcDispatchResponseBuilder(
        ISerializer serializer,
        ConcurrentDictionary<string, IServiceDispatcher> dispatchers,
        Func<Exception, RpcErrorInfo?>? exceptionTransformer = null)
    {
        _serializer = serializer;
        _dispatchers = dispatchers;
        _exceptionTransformer = exceptionTransformer;
    }

    public async ValueTask<RpcDispatchResult> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        RpcStreamingContext streaming,
        CancellationToken ct)
    {
        // request.ServiceName is remote-supplied and can deserialize to null from a hostile/malformed
        // envelope (MessagePack nil). Guard before the dictionary lookup: ConcurrentDictionary throws
        // ArgumentNullException on a null key, which would escape this method (the lookup is outside
        // the try below) and be mis-reported as InternalError instead of a clean ServiceNotFound.
        if (string.IsNullOrEmpty(request.ServiceName) ||
            !_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
        {
            return new RpcDispatchResult(BuildErrorFrame(messageId, RpcErrors.ServiceNotFound()), stream: null);
        }

        var writer = PooledBufferWriter.Rent(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize);
        MessageFramer.WriteFramePrefix(writer, messageId, MessageType.Response);
        var envelopeStart = writer.WrittenCount;
        _serializer.Serialize(writer, new RpcResponse { MessageId = messageId, IsSuccess = true });
        var envelopeLength = writer.WrittenCount - envelopeStart;

        try
        {
            await (request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, writer, streaming, ct)
                : dispatcher.DispatchOnInstanceAsync(
                    request.InstanceId,
                    request.MethodName,
                    payload,
                    _serializer,
                    registry,
                    writer,
                    streaming,
                    ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            writer?.Dispose();
            await streaming.AbandonResponseAsync().ConfigureAwait(false);
            throw;
        }
        catch (ShaRpcProtocolException ex)
        {
            writer?.Dispose();
            await streaming.AbandonResponseAsync().ConfigureAwait(false);
            return new RpcDispatchResult(
                BuildErrorFrame(messageId, RpcErrors.Protocol(ex.Message)),
                stream: null);
        }
        catch (Exception ex)
        {
            writer?.Dispose();
            await streaming.AbandonResponseAsync().ConfigureAwait(false);
            return new RpcDispatchResult(
                BuildErrorFrame(messageId, RpcErrors.FromException(ex, _exceptionTransformer)),
                stream: null);
        }

        if (streaming.Response is { } stream)
        {
            writer.Dispose();
            PooledBufferWriter? responseWriter = null;
            try
            {
                var response = new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = true,
                    Stream = stream.Handle,
                };
                responseWriter = PooledBufferWriter.Rent(
                    MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize);
                MessageFramer.WriteFramePrefix(responseWriter, messageId, MessageType.Response);
                var responseEnvelopeStart = responseWriter.WrittenCount;
                _serializer.Serialize(responseWriter, response);
                var responseEnvelopeLength = responseWriter.WrittenCount - responseEnvelopeStart;
                MessageFramer.CompleteFrame(responseWriter, responseEnvelopeLength);
                var result = new RpcDispatchResult(responseWriter, stream);
                responseWriter = null;
                return result;
            }
            catch
            {
                responseWriter?.Dispose();
                await streaming.AbandonResponseAsync().ConfigureAwait(false);
                throw;
            }
        }

        MessageFramer.CompleteFrame(writer, envelopeLength);
        return new RpcDispatchResult(writer, stream: null);
    }

    public Payload BuildProtocolErrorFrame(int messageId, string errorMessage) =>
        BuildErrorFrame(messageId, RpcErrors.Protocol(errorMessage));

    public Payload BuildErrorFrame(int messageId, RpcError error) =>
        MessageFramer.FrameMessage(
            _serializer,
            messageId,
            MessageType.Error,
            new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = false,
                ErrorMessage = error.Message,
                ErrorType = error.Type,
            },
            ReadOnlySpan<byte>.Empty);
}
