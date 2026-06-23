using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer.Inbound;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Peer;

public sealed partial class RpcPeer
{
    /// <summary>Begins the read loop. Idempotent; safe to call from a fluent chain.</summary>
    public RpcPeer Start()
    {
        EnsureStarted();
        return this;
    }

    private void EnsureStarted()
    {
        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(RpcPeer));
            }

            if (_closed != 0)
            {
                throw new ServiceConnectionException("Connection closed.");
            }

            if (_started != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _started, 1);
            _cts = new CancellationTokenSource();
            _inbound.Start(_cts.Token);
            _readLoop = Task.Run(() => _readLoopRunner.RunAsync(_cts.Token));
        }
    }

    /// <summary>Closes the peer by disposing it; closed peers cannot be restarted.</summary>
    /// <remarks>
    /// Disposal always runs to completion: <paramref name="ct"/> fails fast only before any teardown
    /// begins, and never abandons an in-progress dispose to finish in the background.
    /// </remarks>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Task? readLoop;
        CancellationTokenSource? cts;
        Task disposeTask;
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = 1;
            Interlocked.Exchange(ref _closed, 1);
            _proxyCache = null;
            cts = _cts;
            readLoop = _readLoop;
            cts?.Cancel();
            disposeTask = DisposeCoreAsync(readLoop, cts);
            _disposeTask = disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(Task? readLoop, CancellationTokenSource? cts)
    {
        try
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Channel dispose during peer teardown failed", ex);
        }

        if (readLoop is not null)
        {
            try
            {
                await readLoop.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        await _outbound.StopCancelFramesAsync().ConfigureAwait(false);
        _outbound.FailPending(new ServiceConnectionException("Connection closed."));
        _streams.Stop();
        await _inbound.StopAsync().ConfigureAwait(false);

        _sender.Dispose();
        cts?.Dispose();
    }

    private void RaiseProtocolError(
        int messageId,
        MessageType messageType,
        string message,
        Exception? error) =>
        RpcEventHandlerInvoker.Raise(
            ProtocolError,
            this,
            new RpcProtocolErrorEventArgs(_channel.RemoteEndpoint, messageId, messageType, message, error));

    private void RaiseDispatchError(RpcPeerInboundRequest inbound, Exception error) =>
        RpcEventHandlerInvoker.Raise(
            DispatchError,
            this,
            new RpcDispatchErrorEventArgs(
                _channel.RemoteEndpoint,
                inbound.MessageId,
                inbound.Request.ServiceName,
                inbound.Request.MethodName,
                inbound.Request.InstanceId,
                error));

    private void MarkClosed() => Volatile.Write(ref _closed, 1);

    private void RaiseReadError(Exception error) =>
        RpcEventHandlerInvoker.Raise(
            ReadError,
            this,
            new RpcReadErrorEventArgs(_channel.RemoteEndpoint, error));

    private void RaiseDisconnected(Exception? error) =>
        RpcEventHandlerInvoker.Raise(
            Disconnected,
            this,
            new RpcDisconnectedEventArgs(_channel.RemoteEndpoint, error));
}
