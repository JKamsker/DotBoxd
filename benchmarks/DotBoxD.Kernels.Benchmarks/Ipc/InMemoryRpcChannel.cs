namespace DotBoxD.Kernels.Benchmarks.Ipc;

using System.Threading.Tasks.Sources;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

internal static class InMemoryRpcChannel
{
    public static (IRpcChannel Server, IRpcChannel Client) CreatePair()
    {
        var server = new PipeConnection("memory://client");
        var client = new PipeConnection("memory://server");
        server.Connect(client);
        client.Connect(server);

        return (server, client);
    }

    private sealed class PipeConnection : IRpcFrameChannel, IValueTaskSource<RpcFrame>
    {
        private readonly object _gate = new();
        private readonly Queue<RpcFrame> _inbound = new();
        private ManualResetValueTaskSourceCore<RpcFrame> _receiver;
        private CancellationTokenRegistration _receiveCancellation;
        private PipeConnection? _remote;
        private bool _disposed;
        private bool _waiting;

        public PipeConnection(string remoteEndpoint)
        {
            RemoteEndpoint = remoteEndpoint;
            _receiver.RunContinuationsAsynchronously = true;
        }

        public bool IsConnected => !_disposed;

        public string RemoteEndpoint { get; }

        public void Connect(PipeConnection remote) => _remote = remote;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            SendValueAsync(data, ct).AsTask();

        public ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();

            var remote = _remote ?? throw new InvalidOperationException("Pipe is not connected.");
            var frame = Payload.Rent(data.Length);
            data.Span.CopyTo(frame.Memory.Span);
            return remote.TryEnqueue(new RpcFrame(frame))
                ? default
                : new ValueTask(Task.FromException(new ObjectDisposedException(nameof(PipeConnection))));
        }

        public ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default)
        {
            if (_disposed)
            {
                frame.Dispose();
                throw new ObjectDisposedException(nameof(PipeConnection));
            }

            if (ct.IsCancellationRequested)
            {
                frame.Dispose();
                return new ValueTask(Task.FromCanceled(ct));
            }

            var remote = _remote;
            if (remote is null)
            {
                frame.Dispose();
                throw new InvalidOperationException("Pipe is not connected.");
            }

            return remote.TryEnqueue(new RpcFrame(frame))
                ? default
                : new ValueTask(Task.FromException(new ObjectDisposedException(nameof(PipeConnection))));
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            ReceiveValueAsync(ct).AsTask();

        public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
        {
            var frame = ReceiveFrameValueAsync(ct);
            if (frame.IsCompletedSuccessfully)
            {
                return new ValueTask<Payload>(frame.Result.DetachPayload());
            }

            return AwaitPayloadAsync(frame);
        }

        public ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return new ValueTask<RpcFrame>(Task.FromCanceled<RpcFrame>(ct));
            }

            lock (_gate)
            {
                if (_inbound.TryDequeue(out var frame))
                {
                    return new ValueTask<RpcFrame>(frame);
                }

                if (_disposed)
                {
                    return new ValueTask<RpcFrame>(new RpcFrame(Payload.Empty));
                }

                if (_waiting)
                {
                    throw new InvalidOperationException("Only one pending receive is supported.");
                }

                _receiver.Reset();
                _waiting = true;
                var version = _receiver.Version;
                if (ct.CanBeCanceled)
                {
                    var registration = ct.UnsafeRegister(static state =>
                    {
                        var pending = (PendingReceive)state!;
                        pending.Connection.CancelPendingReceive(pending.Version, pending.CancellationToken);
                    }, new PendingReceive(this, version, ct));
                    if (_waiting)
                    {
                        _receiveCancellation = registration;
                    }
                    else
                    {
                        registration.Dispose();
                    }
                }

                return new ValueTask<RpcFrame>(this, version);
            }
        }

        public ValueTask DisposeAsync()
        {
            PipeConnection? remote;
            lock (_gate)
            {
                if (_disposed)
                {
                    return default;
                }

                _disposed = true;
                remote = _remote;
            }

            remote?.Complete();
            Complete();
            return default;
        }

        public RpcFrame GetResult(short token) => _receiver.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _receiver.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _receiver.OnCompleted(continuation, state, token, flags);

        private bool TryEnqueue(RpcFrame frame)
        {
            var complete = false;
            CancellationTokenRegistration registration = default;
            lock (_gate)
            {
                if (_disposed)
                {
                    frame.Dispose();
                    return false;
                }

                if (_waiting)
                {
                    _waiting = false;
                    registration = _receiveCancellation;
                    _receiveCancellation = default;
                    complete = true;
                }
                else
                {
                    _inbound.Enqueue(frame);
                }
            }

            if (complete)
            {
                registration.Dispose();
                _receiver.SetResult(frame);
            }

            return true;
        }

        private void Complete()
        {
            var complete = false;
            CancellationTokenRegistration registration = default;
            lock (_gate)
            {
                if (_waiting)
                {
                    _waiting = false;
                    registration = _receiveCancellation;
                    _receiveCancellation = default;
                    complete = true;
                }

                while (_inbound.TryDequeue(out var frame))
                {
                    frame.Dispose();
                }
            }

            if (complete)
            {
                registration.Dispose();
                _receiver.SetResult(new RpcFrame(Payload.Empty));
            }
        }

        private void CancelPendingReceive(short version, CancellationToken ct)
        {
            var complete = false;
            lock (_gate)
            {
                if (_waiting && _receiver.Version == version)
                {
                    _waiting = false;
                    _receiveCancellation = default;
                    complete = true;
                }
            }

            if (complete)
            {
                _receiver.SetException(new OperationCanceledException(ct));
            }
        }

        private static async ValueTask<Payload> AwaitPayloadAsync(ValueTask<RpcFrame> frame)
        {
            var received = await frame.ConfigureAwait(false);
            return received.DetachPayload();
        }

        private sealed record PendingReceive(
            PipeConnection Connection,
            short Version,
            CancellationToken CancellationToken);
    }
}
