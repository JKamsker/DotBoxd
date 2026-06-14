using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerInboundDispatcher
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

    public async ValueTask<bool> AcceptRequestAsync(
        RpcFrame frame,
        int messageId,
        CancellationToken loopCt)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        if (!TryCreateInboundRequest(
            frame,
            messageId,
            loopCt,
            out var inbound,
            out var protocolError,
            out var protocolException))
        {
            if (protocolError is not null)
            {
                _protocolError(messageId, MessageType.Request, protocolError, protocolException);
                using var errorFrame = _responseBuilder.BuildProtocolErrorFrame(messageId, protocolError);
                await _sendAsync(errorFrame.Memory, loopCt).ConfigureAwait(false);
            }

            return false;
        }

        if (_queue is null)
        {
            StartRequest(inbound);
            return true;
        }

        var result = await _queue.EnqueueAsync(inbound, loopCt).ConfigureAwait(false);
        if (result == InboundEnqueueResult.Dropped)
        {
            // The request was shed under backpressure. Reply with an explicit queue-full error so the
            // caller fails fast instead of waiting out its whole request timeout.
            await SendQueueFullErrorAsync(messageId, loopCt).ConfigureAwait(false);
        }

        return result == InboundEnqueueResult.Accepted;
    }

    public ValueTask<bool> AcceptRequestAsync(
        Payload frame,
        int messageId,
        CancellationToken loopCt) =>
        AcceptRequestAsync(new RpcFrame(frame), messageId, loopCt);

    private async Task SendQueueFullErrorAsync(int messageId, CancellationToken ct)
    {
        try
        {
            using var errorFrame = _responseBuilder.BuildErrorFrame(messageId, RpcErrors.QueueFull());
            await _sendAsync(errorFrame.Memory, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: if the notification cannot be sent, the caller still falls back to its timeout.
        }
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

    private bool TryCreateInboundRequest(
        RpcFrame frame,
        int messageId,
        CancellationToken loopCt,
        out RpcPeerInboundRequest inbound,
        out string? protocolError,
        out Exception? protocolException)
    {
        inbound = default;
        protocolException = null;
        if (!RpcPeerInboundRequestReader.TryRead(
            frame,
            _serializer,
            out var request,
            out var payload,
            out protocolError,
            out protocolException))
        {
            return false;
        }

        if (!RpcStreamValidation.TryValidateInboundHandles(request.Streams, out protocolError))
        {
            return false;
        }

        if (loopCt.IsCancellationRequested)
        {
            protocolError = null;
            return false;
        }

        var requestCts = !_disableInboundRequestCancellation ||
            request.Streams is not null ||
            _responseBuilder.RequiresStreamingContext(request)
                ? new CancellationTokenSource()
                : null;
        if (!_activeInbound.TryAdd(messageId, requestCts))
        {
            requestCts?.Dispose();
            protocolError = "Duplicate request message id.";
            return false;
        }

        // Re-check after adding: if StopAsync or loop cancellation ran between our initial checks
        // and TryAdd, the CTS was missed by the active-request cancellation loop.
        if (Volatile.Read(ref _stopped) != 0 || loopCt.IsCancellationRequested)
        {
            _activeInbound.Remove(messageId, requestCts);
            requestCts?.Dispose();
            protocolError = null;
            return false;
        }

        inbound = new RpcPeerInboundRequest(frame, request, messageId, payload, requestCts);
        try
        {
            _streams.RegisterInbound(request.Streams, inbound.CancellationToken);
        }
        catch (ShaRpcProtocolException ex)
        {
            _activeInbound.Remove(messageId, requestCts);
            requestCts?.Dispose();
            inbound = default;
            protocolError = ex.Message;
            protocolException = ex;
            return false;
        }

        return true;
    }

    private void StartRequest(RpcPeerInboundRequest inbound)
    {
        if (!TryEnterActiveRequest())
        {
            inbound.Frame.Dispose();
            ReleaseRequest(inbound);
            return;
        }

        _ = ProcessTrackedRequestAsync(inbound);
    }

    private async Task ProcessTrackedRequestAsync(RpcPeerInboundRequest inbound)
    {
        try
        {
            await ProcessRequestAsync(inbound).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Tracked inbound request failed", ex);
        }
        finally
        {
            CompleteActiveRequest();
        }
    }

    private async Task ProcessRequestAsync(RpcPeerInboundRequest inbound)
    {
        var releaseRequest = true;
        RpcStreamingContext? streaming = null;
        try
        {
            using (inbound.Frame)
            {
                streaming = _responseBuilder.RequiresStreamingContext(inbound.Request)
                    ? new RpcStreamingContext(
                        _streams,
                        _serializer,
                        inbound.CancellationToken,
                        inbound.Request.Streams)
                    : RpcStreamingContext.Disabled;
                using var response = await _responseBuilder.BuildAsync(
                    inbound.Request,
                    inbound.MessageId,
                    inbound.Body,
                    streaming,
                    inbound.CancellationToken).ConfigureAwait(false);
                var responseStream = response.Stream;
                try
                {
                    if (_sendFrameAsync is not null &&
                        response.TryDetachWriter(out var responseWriter))
                    {
                        await _sendFrameAsync(responseWriter, inbound.CancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _sendAsync(response.FrameMemory, inbound.CancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    if (responseStream is not null)
                    {
                        await streaming.AbandonResponseAsync().ConfigureAwait(false);
                    }

                    throw;
                }

                if (responseStream is not null)
                {
                    if (StartResponseStream(inbound, responseStream, streaming))
                    {
                        releaseRequest = false;
                    }
                    else
                    {
                        await streaming.AbandonResponseAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (inbound.IsCancellationRequested)
        {
            // Cancelled work sends no response frame.
        }
        catch (Exception ex)
        {
            _dispatchError(inbound, ex);
            RpcDiagnostics.Report("Inbound request dispatch failed", ex);
            try
            {
                using var errorFrame = _responseBuilder.BuildErrorFrame(
                    inbound.MessageId,
                    RpcErrors.FromException(ex, _exceptionTransformer));
                await _sendAsync(errorFrame.Memory, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort error response.
            }
        }
        finally
        {
            if (releaseRequest)
            {
                ReleaseRequest(inbound);
            }
        }
    }

    private bool StartResponseStream(
        RpcPeerInboundRequest inbound,
        RpcStreamAttachment stream,
        RpcStreamingContext streaming)
    {
        if (!TryEnterActiveStream())
        {
            return false;
        }

        _ = ProcessResponseStreamAsync(inbound, stream, streaming);
        return true;
    }

    private async Task ProcessResponseStreamAsync(
        RpcPeerInboundRequest inbound,
        RpcStreamAttachment stream,
        RpcStreamingContext streaming)
    {
        var registered = false;
        try
        {
            await using var outbound = _streams.RegisterOutbound(stream, inbound.CancellationToken);
            registered = true;
            outbound.Start();
            await outbound.WaitAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!registered || inbound.IsCancellationRequested)
        {
            if (!registered)
            {
                await stream.DisposeSourceBestEffortAsync("Inbound response stream source cleanup failed")
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (!registered)
            {
                await stream.DisposeSourceBestEffortAsync("Inbound response stream source cleanup failed")
                    .ConfigureAwait(false);
            }

            _dispatchError(inbound, ex);
            RpcDiagnostics.Report("Inbound response stream failed", ex);
        }
        finally
        {
            CompleteActiveStream();
            ReleaseRequest(inbound);
        }
    }

    private void ReleaseRequest(RpcPeerInboundRequest inbound)
    {
        if (inbound.Request.Streams is { } streams)
        {
            foreach (var stream in streams)
            {
                _streams.RemoveInbound(stream.StreamId);
            }
        }

        _activeInbound.Remove(inbound.MessageId, inbound.RequestCts);
        inbound.RequestCts?.Dispose();
    }

    private bool TryEnterActiveRequest() =>
        TryEnterActiveOperation(ref _activeRequestCount, ref _activeRequestsDrained);

    private bool TryEnterActiveStream() =>
        TryEnterActiveOperation(ref _activeStreamCount, ref _activeStreamsDrained);

    private bool TryEnterActiveOperation(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        Interlocked.Increment(ref count);
        if (Volatile.Read(ref _stopped) == 0)
        {
            return true;
        }

        CompleteActiveOperation(ref count, ref drained);
        return false;
    }

    private Task WaitForActiveRequestsAsync() =>
        WaitForActiveOperationsAsync(ref _activeRequestCount, ref _activeRequestsDrained);

    private Task WaitForActiveStreamsAsync() =>
        WaitForActiveOperationsAsync(ref _activeStreamCount, ref _activeStreamsDrained);

    private static Task WaitForActiveOperationsAsync(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Volatile.Read(ref count) == 0)
        {
            return Task.CompletedTask;
        }

        var signal = Volatile.Read(ref drained);
        if (signal is null)
        {
            var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = Interlocked.CompareExchange(ref drained, created, null) ?? created;
        }

        if (Volatile.Read(ref count) == 0)
        {
            signal.TrySetResult(true);
        }

        return signal.Task;
    }

    private void CompleteActiveRequest() =>
        CompleteActiveOperation(ref _activeRequestCount, ref _activeRequestsDrained);

    private void CompleteActiveStream() =>
        CompleteActiveOperation(ref _activeStreamCount, ref _activeStreamsDrained);

    private static void CompleteActiveOperation(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Interlocked.Decrement(ref count) == 0)
        {
            Volatile.Read(ref drained)?.TrySetResult(true);
        }
    }
}
