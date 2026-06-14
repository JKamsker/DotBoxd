using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcRemoteAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly RpcStreamReceiver _receiver;
    private readonly ISerializer _serializer;
    private int _enumerated;

    public RpcRemoteAsyncEnumerable(RpcStreamReceiver receiver, ISerializer serializer)
    {
        _receiver = receiver;
        _serializer = serializer;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) != 0)
        {
            throw new InvalidOperationException("A remote RPC stream can only be enumerated once.");
        }

        return new Enumerator(_receiver, _serializer, cancellationToken);
    }

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly RpcStreamReceiver _receiver;
        private readonly ISerializer _serializer;
        private readonly CancellationToken _ct;
        private bool _completed;

        public Enumerator(
            RpcStreamReceiver receiver,
            ISerializer serializer,
            CancellationToken ct)
        {
            _receiver = receiver;
            _serializer = serializer;
            _ct = ct;
        }

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            var chunk = await _receiver.ReadChunkAsync(_ct).ConfigureAwait(false);
            if (chunk is null)
            {
                _completed = true;
                return false;
            }

            using (chunk)
            {
                Current = _serializer.Deserialize<T>(chunk.Payload);
                return true;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                return _receiver.CancelAsync();
            }

            return default;
        }
    }
}
