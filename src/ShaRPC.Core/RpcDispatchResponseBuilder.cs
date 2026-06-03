using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

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

    public async ValueTask<Payload> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        CancellationToken ct)
    {
        // request.ServiceName is remote-supplied and can deserialize to null from a hostile/malformed
        // envelope (MessagePack nil). Guard before the dictionary lookup: ConcurrentDictionary throws
        // ArgumentNullException on a null key, which would escape this method (the lookup is outside
        // the try below) and be mis-reported as InternalError instead of a clean ServiceNotFound.
        if (string.IsNullOrEmpty(request.ServiceName) ||
            !_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
        {
            return BuildErrorFrame(messageId, RpcErrors.ServiceNotFound());
        }

        using var writer = new PooledBufferWriter(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize);
        MessageFramer.WriteFramePrefix(writer, messageId, MessageType.Response);
        var envelopeStart = writer.WrittenCount;
        _serializer.Serialize(writer, new RpcResponse { MessageId = messageId, IsSuccess = true });
        var envelopeLength = writer.WrittenCount - envelopeStart;

        try
        {
            await (request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, writer, ct)
                : dispatcher.DispatchOnInstanceAsync(
                    request.InstanceId,
                    request.MethodName,
                    payload,
                    _serializer,
                    registry,
                    writer,
                    ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildErrorFrame(messageId, RpcErrors.FromException(ex, _exceptionTransformer));
        }

        return MessageFramer.FinishFrame(writer, envelopeLength);
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
