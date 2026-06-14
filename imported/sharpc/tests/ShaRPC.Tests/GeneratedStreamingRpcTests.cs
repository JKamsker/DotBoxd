using System.Runtime.CompilerServices;
using ShaRPC.Core;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests.GeneratedFixtures;
using Xunit;

namespace ShaRPC.Tests;

public sealed class GeneratedStreamingRpcTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task GeneratedProxyAndDispatcher_StreamResponsesAndArguments()
    {
        var service = new GeneratedStreamingService();
        var serializer = new MessagePackRpcSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Provide<IGeneratedStreamingService>(service)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();
        var proxy = client.Get<IGeneratedStreamingService>();

        await using var enumerator = proxy.Numbers().GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal(1, enumerator.Current);
        service.ReleaseNumbers();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal(2, enumerator.Current);
        Assert.False(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));

        var taskWrapped = await proxy.NumbersAsync().WaitAsync(Timeout);
        await service.TaskWrappedNumbersInvoked.Task.WaitAsync(Timeout);
        var wrappedValues = new List<int>();
        using var taskWrappedEnumerationCts = new CancellationTokenSource(Timeout);
        await foreach (var item in taskWrapped.WithCancellation(taskWrappedEnumerationCts.Token))
        {
            wrappedValues.Add(item);
        }

        Assert.Equal(new[] { 3, 4 }, wrappedValues);

        await using var bytes = new MemoryStream(new byte[] { 5, 6, 7 });
        var uploaded = await proxy.UploadAsync(bytes, UploadItems()).WaitAsync(Timeout);

        Assert.Equal(5 + 6 + 7 + 10 + 20, uploaded);

        await using var lazyBytes = new MemoryStream(new byte[] { 1, 2 });
        var lazyUpload = proxy.StreamUploadAsync(lazyBytes, UploadItems());
        Assert.False(service.StreamUploadInvoked.Task.IsCompleted);

        var firstLazyValues = await ReadAllAsync(lazyUpload);
        Assert.Equal(new[] { 1 + 2 + 10 + 20 }, firstLazyValues);

        lazyBytes.Position = 0;
        var secondLazyValues = await ReadAllAsync(lazyUpload);
        Assert.Equal(new[] { 1 + 2 + 10 + 20 }, secondLazyValues);
        Assert.Equal(2, service.StreamUploadInvocationCount);
    }

    private static async IAsyncEnumerable<int> UploadItems(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return 10;
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        yield return 20;
    }

    private static async Task<List<int>> ReadAllAsync(IAsyncEnumerable<int> source)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var values = new List<int>();
        await foreach (var item in source.WithCancellation(cts.Token))
        {
            values.Add(item);
        }

        return values;
    }

    private sealed class GeneratedStreamingService : IGeneratedStreamingService
    {
        private readonly TaskCompletionSource _numbersGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TaskWrappedNumbersInvoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StreamUploadInvoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StreamUploadInvocationCount { get; private set; }

        public void ReleaseNumbers() => _numbersGate.TrySetResult();

        public async IAsyncEnumerable<int> Numbers(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return 1;
            await _numbersGate.Task.WaitAsync(ct).ConfigureAwait(false);
            yield return 2;
        }

        public Task<IAsyncEnumerable<int>> NumbersAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            TaskWrappedNumbersInvoked.TrySetResult();
            return Task.FromResult(WrappedNumbers(ct));
        }

        public async Task<int> UploadAsync(
            Stream bytes,
            IAsyncEnumerable<int> items,
            CancellationToken ct = default)
        {
            var sum = 0;
            var buffer = new byte[4];
            int read;
            while ((read = await bytes.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                sum += buffer.AsSpan(0, read).ToArray().Sum(static b => b);
            }

            await foreach (var item in items.WithCancellation(ct).ConfigureAwait(false))
            {
                sum += item;
            }

            return sum;
        }

        public async IAsyncEnumerable<int> StreamUploadAsync(
            Stream bytes,
            IAsyncEnumerable<int> items,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            StreamUploadInvocationCount++;
            StreamUploadInvoked.TrySetResult();
            yield return await UploadAsync(bytes, items, ct).ConfigureAwait(false);
        }

        private static async IAsyncEnumerable<int> WrappedNumbers(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return 3;
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return 4;
        }
    }
}
