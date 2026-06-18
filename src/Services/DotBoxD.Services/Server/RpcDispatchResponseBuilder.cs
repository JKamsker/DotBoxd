using System.Collections.Frozen;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Remote;

namespace DotBoxD.Services.Server;

internal sealed class RpcDispatchResponseBuilder
{
    private readonly ISerializer _serializer;
    private readonly IReadOnlyDictionary<string, IServiceDispatcher> _dispatchers;
    private readonly Func<Exception, RpcErrorInfo?>? _exceptionTransformer;
    private FrozenDictionary<string, IServiceDispatcher>? _frozenDispatchers;

    public RpcDispatchResponseBuilder(
        ISerializer serializer,
        IReadOnlyDictionary<string, IServiceDispatcher> dispatchers,
        Func<Exception, RpcErrorInfo?>? exceptionTransformer = null)
    {
        _serializer = serializer;
        _dispatchers = dispatchers;
        _exceptionTransformer = exceptionTransformer;
    }

    public void FreezeDispatchers()
    {
        if (_frozenDispatchers is null)
        {
            _frozenDispatchers = _dispatchers.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    public bool RequiresStreamingContext(RpcRequest request)
    {
        if (!TryResolveDispatcher(request, out var dispatcher))
        {
            return false;
        }

        return dispatcher is not INonStreamingServiceDispatcher;
    }

    public bool TryResolveDispatcher(RpcRequest request, out IServiceDispatcher dispatcher)
    {
        dispatcher = null!;
        return !string.IsNullOrEmpty(request.ServiceName) &&
            TryGetDispatcher(request.ServiceName, out dispatcher);
    }

    public ValueTask<RpcDispatchResult> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        RpcStreamingContext streaming,
        CancellationToken ct)
    {
        var dispatcher = TryResolveDispatcher(request, out var resolved)
            ? resolved
            : null;
        return BuildAsync(request, messageId, payload, registry, streaming, dispatcher, ct);
    }

    public async ValueTask<RpcDispatchResult> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        RpcStreamingContext streaming,
        IServiceDispatcher? dispatcher,
        CancellationToken ct)
    {
        // request.ServiceName is remote-supplied and can deserialize to null from a hostile/malformed
        // envelope (MessagePack nil). Guard before the dictionary lookup so that malformed input is
        // reported as ServiceNotFound instead of escaping as an internal lookup error.
        if (dispatcher is null)
        {
            return new RpcDispatchResult(BuildErrorFrame(messageId, RpcErrors.ServiceNotFound()), stream: null);
        }

        var writer = MessageFramer.RentFrameWriter();
        MessageFramer.WriteFramePrefix(writer, messageId, MessageType.Response);
        var envelopeStart = writer.WrittenCount;
        _serializer.Serialize(writer, new RpcResponse { MessageId = messageId, IsSuccess = true });
        var envelopeLength = writer.WrittenCount - envelopeStart;

        try
        {
            await DispatchAsync(
                dispatcher,
                request,
                payload,
                registry,
                writer,
                streaming,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            writer?.Dispose();
            await streaming.AbandonResponseAsync().ConfigureAwait(false);
            throw;
        }
        catch (ServiceProtocolException ex)
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
                responseWriter = MessageFramer.RentFrameWriter();
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

    private bool TryGetDispatcher(string serviceName, out IServiceDispatcher dispatcher)
    {
        var frozenDispatchers = _frozenDispatchers;
        if (frozenDispatchers is not null)
        {
            return frozenDispatchers.TryGetValue(serviceName, out dispatcher!);
        }

        return _dispatchers.TryGetValue(serviceName, out dispatcher!);
    }

    private Task DispatchAsync(
        IServiceDispatcher dispatcher,
        RpcRequest request,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        PooledBufferWriter writer,
        RpcStreamingContext streaming,
        CancellationToken ct)
    {
        if (dispatcher is INonStreamingServiceDispatcher)
        {
            return request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, writer, ct)
                : dispatcher.DispatchOnInstanceAsync(
                    request.InstanceId,
                    request.MethodName,
                    payload,
                    _serializer,
                    registry,
                    writer,
                    ct);
        }

        return request.InstanceId is null
            ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, writer, streaming, ct)
            : dispatcher.DispatchOnInstanceAsync(
                request.InstanceId,
                request.MethodName,
                payload,
                _serializer,
                registry,
                writer,
                streaming,
                ct);
    }
}
