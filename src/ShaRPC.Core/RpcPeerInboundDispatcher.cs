using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeInbound = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
    private readonly ConcurrentDictionary<int, Task> _activeStreamTasks = new();
    private readonly InstanceRegistry _registry = new();
    private readonly ISerializer _serializer;
    private readonly RpcPeerResponseBuilder _responseBuilder;
    private readonly RpcStreamManager _streams;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Action<int, MessageType, string, Exception?> _protocolError;
    private readonly Action<RpcPeerInboundRequest, Exception> _dispatchError;
    private readonly Func<Exception, RpcErrorInfo?>? _exceptionTransformer;
    private readonly RpcPeerInboundRequestQueue? _queue;
    private int _stopped;

    public RpcPeerInboundDispatcher(
        ISerializer serializer,
        RpcPeerOptions options,
        RpcStreamManager streams,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
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
        _protocolError = protocolError;
        _dispatchError = dispatchError;
        _exceptionTransformer = options.ExceptionTransformer;
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
            ReleaseRequest);
    }

    public void Start(CancellationToken loopCt)
    {
        _queue?.Start(loopCt);
    }

    internal int ActiveInboundCount => _activeInbound.Count;

    public void AddDispatcher(IServiceDispatcher dispatcher)
    {
        if (!_dispatchers.TryAdd(dispatcher.ServiceName, dispatcher))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already provided.");
        }
    }

    public async ValueTask<bool> AcceptRequestAsync(
        Payload frame,
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
        if (_activeInbound.TryGetValue(messageId, out var requestCts))
        {
            SafeCancel(requestCts);
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        foreach (var requestCts in _activeInbound.Values)
        {
            SafeCancel(requestCts);
        }

        if (_queue is not null)
        {
            await _queue.StopAsync().ConfigureAwait(false);
        }

        if (!_activeTasks.IsEmpty)
        {
            await ObserveShutdownAsync(Task.WhenAll(_activeTasks.Values)).ConfigureAwait(false);
        }

        if (!_activeStreamTasks.IsEmpty)
        {
            await ObserveShutdownAsync(Task.WhenAll(_activeStreamTasks.Values)).ConfigureAwait(false);
        }

        await _registry.ReleaseAllAsync().ConfigureAwait(false);
    }

    private bool TryCreateInboundRequest(
        Payload frame,
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

        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        if (!_activeInbound.TryAdd(messageId, requestCts))
        {
            requestCts.Dispose();
            protocolError = "Duplicate request message id.";
            return false;
        }

        // Re-check after adding: if StopAsync ran between our initial check and TryAdd,
        // the CTS was missed by StopAsync's cancellation loop.
        if (Volatile.Read(ref _stopped) != 0)
        {
            _activeInbound.TryRemove(messageId, out _);
            requestCts.Dispose();
            protocolError = null;
            return false;
        }

        inbound = new RpcPeerInboundRequest(frame, request, messageId, payload, requestCts);
        try
        {
            _streams.RegisterInbound(request.Streams, requestCts.Token);
        }
        catch (ShaRpcProtocolException ex)
        {
            _activeInbound.TryRemove(messageId, out _);
            requestCts.Dispose();
            inbound = default;
            protocolError = ex.Message;
            protocolException = ex;
            return false;
        }

        return true;
    }

    private void StartRequest(RpcPeerInboundRequest inbound)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_activeTasks.TryAdd(inbound.MessageId, completion.Task))
        {
            Debug.Assert(false, "Duplicate message ID in _activeTasks");
            inbound.Frame.Dispose();
            ReleaseRequest(inbound);
            return;
        }

        // Re-check after registering: if StopAsync ran between AcceptRequestAsync's check and this
        // TryAdd, its _activeTasks snapshot missed this entry, so the dispatch would run after
        // StopAsync returned (the contract the queued path upholds via _inFlight). StopAsync sets
        // _stopped before snapshotting, so either it now sees this task (and awaits it) or we observe
        // _stopped here and abandon the dispatch — closing the window either way.
        if (Volatile.Read(ref _stopped) != 0)
        {
            _activeTasks.TryRemove(inbound.MessageId, out _);
            completion.TrySetResult(false);
            inbound.Frame.Dispose();
            ReleaseRequest(inbound);
            return;
        }

        _ = ProcessTrackedRequestAsync(inbound, completion);
    }

    private async Task ProcessTrackedRequestAsync(
        RpcPeerInboundRequest inbound,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            await ProcessRequestAsync(inbound).ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            RpcDiagnostics.Report("Tracked inbound request failed", ex);
        }
        finally
        {
            _activeTasks.TryRemove(inbound.MessageId, out _);
        }
    }

    private async Task ProcessRequestAsync(RpcPeerInboundRequest inbound)
    {
        var releaseRequest = true;
        try
        {
            using (inbound.Frame)
            {
                var streaming = new RpcStreamingContext(
                    _streams,
                    _serializer,
                    inbound.RequestCts.Token);
                using var response = await _responseBuilder.BuildAsync(
                    inbound.Request,
                    inbound.MessageId,
                    inbound.Body,
                    streaming,
                    inbound.RequestCts.Token).ConfigureAwait(false);
                var responseStream = response.Stream;
                try
                {
                    await _sendAsync(response.Frame.Memory, inbound.RequestCts.Token).ConfigureAwait(false);
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
                    StartResponseStream(inbound, responseStream);
                    releaseRequest = false;
                }
            }
        }
        catch (OperationCanceledException) when (inbound.RequestCts.IsCancellationRequested)
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

    private void StartResponseStream(RpcPeerInboundRequest inbound, RpcStreamAttachment stream)
    {
        var task = ProcessResponseStreamAsync(inbound, stream);
        if (!_activeStreamTasks.TryAdd(inbound.MessageId, task))
        {
            RpcDiagnostics.Report(
                "Duplicate inbound response stream task",
                new InvalidOperationException("Duplicate message ID in _activeStreamTasks."));
        }
        else if (task.IsCompleted)
        {
            _activeStreamTasks.TryRemove(inbound.MessageId, out _);
        }
    }

    private async Task ProcessResponseStreamAsync(RpcPeerInboundRequest inbound, RpcStreamAttachment stream)
    {
        var registered = false;
        try
        {
            await using var outbound = _streams.RegisterOutbound(
                new[] { stream },
                inbound.RequestCts.Token);
            registered = true;
            outbound.Start();
            await outbound.WaitAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!registered || inbound.RequestCts.IsCancellationRequested)
        {
            if (!registered)
            {
                await stream.DisposeSourceAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (!registered)
            {
                await stream.DisposeSourceAsync().ConfigureAwait(false);
            }

            _dispatchError(inbound, ex);
            RpcDiagnostics.Report("Inbound response stream failed", ex);
        }
        finally
        {
            _activeStreamTasks.TryRemove(inbound.MessageId, out _);
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

        _activeInbound.TryRemove(inbound.MessageId, out _);
        inbound.RequestCts.Dispose();
    }

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed while the connection was closing.
        }
    }

    private static async Task ObserveShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Individual request tasks observe their own failures.
        }
    }

}
