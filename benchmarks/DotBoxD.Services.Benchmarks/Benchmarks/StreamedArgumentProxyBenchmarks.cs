using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using Shared;

namespace DotBoxD.Services.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class StreamedArgumentProxyBenchmarks
{
    private readonly ImmediateInvoker _invoker = new();
    private readonly MemoryStream _bytes = new(new byte[] { 1, 2, 3 });
    private readonly IAsyncEnumerable<int> _items = EmptyItems();
    private IStreamedArgumentBenchmarkService _service = null!;

    [GlobalSetup]
    public void Setup() =>
        _service = new StreamedArgumentBenchmarkServiceProxy(_invoker);

    [Benchmark]
    public Task<int> SingleStreamUpload() =>
        _service.UploadBytesAsync(_bytes);

    [Benchmark]
    public Task<int> TwoStreamUpload() =>
        _service.UploadBothAsync(_bytes, _items);

    private static async IAsyncEnumerable<int> EmptyItems(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        yield break;
    }

    private sealed class ImmediateInvoker : IRpcInvoker
    {
        private static readonly Task<int> Result = Task.FromResult(42);
        private int _nextStreamId;

        public RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
            new(Interlocked.Increment(ref _nextStreamId), kind);

        public void ReleaseStream(RpcStreamHandle handle)
        {
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment stream,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return Completed<TResponse>();
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[] streams,
            CancellationToken ct = default)
        {
            if (streams.Length != 2)
            {
                throw new InvalidOperationException("Expected the two-stream benchmark path.");
            }

            return Completed<TResponse>();
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            Task.FromException<TResponse>(new NotSupportedException());

        public Task<TResponse> InvokeAsync<TResponse>(
            string service,
            string method,
            CancellationToken ct = default) =>
            Task.FromException<TResponse>(new NotSupportedException());

        public Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        public Task InvokeAsync(
            string service,
            string method,
            CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            Task.FromException<TResponse>(new NotSupportedException());

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            Task.FromException<TResponse>(new NotSupportedException());

        public Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        private static Task<TResponse> Completed<TResponse>() =>
            typeof(TResponse) == typeof(int)
                ? (Task<TResponse>)(object)Result
                : Task.FromException<TResponse>(new NotSupportedException());
    }
}
