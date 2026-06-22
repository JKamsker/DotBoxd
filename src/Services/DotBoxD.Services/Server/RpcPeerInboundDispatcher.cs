using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Peer.Inbound;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Server;

internal sealed partial class RpcPeerInboundDispatcher
{
    private readonly Dictionary<string, IServiceDispatcher> _dispatchers = new(StringComparer.Ordinal);
    private readonly RpcPeerActiveInboundRequests _activeInbound = new();
    private readonly InstanceRegistry _registry = new();
    private readonly ISerializer _serializer;
    private readonly RpcPeerResponseBuilder _responseBuilder;
    private readonly RpcStreamManager _streams;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Func<PooledBufferWriter, CancellationToken, ValueTask>? _sendFrameAsync;
    private readonly Action<int, MessageType, string, Exception?> _protocolError;
    private readonly Action<RpcPeerInboundRequest, Exception> _dispatchError;
    private readonly Func<Exception, RpcErrorInfo?>? _exceptionTransformer;
    private readonly bool _disableInboundRequestCancellation;
    private readonly RpcPeerInboundRequestQueue? _queue;
    private TaskCompletionSource<bool>? _activeRequestsDrained;
    private TaskCompletionSource<bool>? _activeStreamsDrained;
    private CancellationTokenRegistration _loopCancellation;
    private int _activeRequestCount;
    private int _activeStreamCount;
    private int _dispatchersFrozen;
    private int _stopped;

    public RpcPeerInboundDispatcher(
        ISerializer serializer,
        RpcPeerOptions options,
        RpcStreamManager streams,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Action<int, MessageType, string, Exception?> protocolError,
        Action<RpcPeerInboundRequest, Exception> dispatchError)
        : this(serializer, options, streams, sendAsync, sendFrameAsync: null, protocolError, dispatchError)
    {
    }

    public RpcPeerInboundDispatcher(
        ISerializer serializer,
        RpcPeerOptions options,
        RpcStreamManager streams,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<PooledBufferWriter, CancellationToken, ValueTask>? sendFrameAsync,
        Action<int, MessageType, string, Exception?> protocolError,
        Action<RpcPeerInboundRequest, Exception> dispatchError)
    {
        _serializer = serializer;
        _responseBuilder = new RpcPeerResponseBuilder(
            serializer,
            _registry,
            _dispatchers,
            options.RejectInboundCalls,
            options.ExceptionTransformer);
        _streams = streams;
        _sendAsync = sendAsync;
        _sendFrameAsync = sendFrameAsync;
        _protocolError = protocolError;
        _dispatchError = dispatchError;
        _exceptionTransformer = options.ExceptionTransformer;
        _disableInboundRequestCancellation = options.DisableInboundRequestCancellation;
        if (options.InboundQueueCapacity is not { } capacity)
        {
            return;
        }
        _queue = new RpcPeerInboundRequestQueue(
            capacity,
            options.QueueFullMode,
            options.MaxConcurrentInboundDispatch,
            options.MaxInboundBytes,
            ProcessRequestAsync,
            inbound => ReleaseRequest(inbound));
    }

    public void Start(CancellationToken loopCt)
    {
        _responseBuilder.FreezeDispatchers();
        Volatile.Write(ref _dispatchersFrozen, 1);
        if (loopCt.CanBeCanceled)
        {
            _loopCancellation = loopCt.Register(
                static state => ((RpcPeerActiveInboundRequests)state!).CancelAll(),
                _activeInbound);
        }

        _queue?.Start(loopCt);
    }

    internal int ActiveInboundCount => _activeInbound.Count;

    public void AddDispatcher(IServiceDispatcher dispatcher)
    {
        if (Volatile.Read(ref _dispatchersFrozen) != 0)
        {
            throw new InvalidOperationException("Services must be added before the inbound dispatcher starts.");
        }

        if (_dispatchers.ContainsKey(dispatcher.ServiceName))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already provided.");
        }

        _dispatchers.Add(dispatcher.ServiceName, dispatcher);
    }

    public void Cancel(int messageId)
    {
        _activeInbound.Cancel(messageId);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _loopCancellation.Dispose();
        _activeInbound.CancelAll();

        if (_queue is not null)
        {
            await _queue.StopAsync().ConfigureAwait(false);
        }

        await WaitForActiveRequestsAsync().ConfigureAwait(false);
        await WaitForActiveStreamsAsync().ConfigureAwait(false);

        await _registry.ReleaseAllAsync().ConfigureAwait(false);
    }

}
