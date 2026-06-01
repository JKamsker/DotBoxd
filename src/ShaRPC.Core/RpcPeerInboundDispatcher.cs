using System.Collections.Concurrent;
using System.Diagnostics;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerInboundDispatcher
{
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeInbound = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
    private readonly InstanceRegistry _registry = new();
    private readonly ISerializer _serializer;
    private readonly RpcPeerResponseBuilder _responseBuilder;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Action<int, MessageType, string, Exception?> _protocolError;
    private readonly Action<RpcPeerInboundRequest, Exception> _dispatchError;
    private readonly RpcPeerInboundRequestQueue? _queue;
    private int _stopped;

    public RpcPeerInboundDispatcher(
        ISerializer serializer,
        RpcPeerOptions options,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Action<int, MessageType, string, Exception?> protocolError,
        Action<RpcPeerInboundRequest, Exception> dispatchError)
    {
        _serializer = serializer;
        _responseBuilder = new RpcPeerResponseBuilder(
            serializer,
            _registry,
            _dispatchers,
            options.RejectInboundCalls);
        _sendAsync = sendAsync;
        _protocolError = protocolError;
        _dispatchError = dispatchError;
        if (options.InboundQueueCapacity is not { } capacity)
        {
            return;
        }
        _queue = new RpcPeerInboundRequestQueue(
            capacity,
            options.QueueFullMode,
            ProcessRequestAsync,
            ReleaseRequest);
    }

    public void Start(CancellationToken loopCt)
    {
        _queue?.Start(loopCt);
    }

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

        return await _queue.EnqueueAsync(inbound, loopCt).ConfigureAwait(false);
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

        _registry.ReleaseAll();
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
        try
        {
            using (inbound.Frame)
            {
                using var responseFrame = await _responseBuilder.BuildAsync(
                    inbound.Request,
                    inbound.MessageId,
                    inbound.Body,
                    inbound.RequestCts.Token).ConfigureAwait(false);
                await _sendAsync(responseFrame.Memory, inbound.RequestCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (inbound.RequestCts.IsCancellationRequested)
        {
            // Cancelled work sends no response frame.
        }
        catch (Exception ex)
        {
            _dispatchError(inbound, ex);
            RpcDiagnostics.Report(
                $"Inbound request {inbound.Request.ServiceName}.{inbound.Request.MethodName} failed",
                ex);
        }
        finally
        {
            ReleaseRequest(inbound);
        }
    }

    private void ReleaseRequest(RpcPeerInboundRequest inbound)
    {
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
