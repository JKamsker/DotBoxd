using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Server;

public sealed class RpcDispatchResponseBuilderLookupTests
{
    [Fact]
    public async Task BuildAsync_WithResolvedDispatcher_DoesNotLookupDispatcherAgain()
    {
        var dispatcher = new NoopDispatcher("LookupTarget");
        var dispatchers = new CountingDispatcherMap(dispatcher);
        var builder = new RpcDispatchResponseBuilder(new MessagePackRpcSerializer(), dispatchers);
        var request = new RpcRequest
        {
            MessageId = 1,
            ServiceName = dispatcher.ServiceName,
            MethodName = "Noop",
        };

        Assert.True(builder.TryResolveDispatcher(request, out var resolved));
        Assert.Same(dispatcher, resolved);
        Assert.Equal(1, dispatchers.TryGetValueCount);

        using var result = await builder.BuildAsync(
            request,
            messageId: 1,
            ReadOnlyMemory<byte>.Empty,
            new InstanceRegistry(),
            RpcStreamingContext.Disabled,
            resolved,
            CancellationToken.None);

        Assert.Equal(1, dispatchers.TryGetValueCount);
        Assert.True(MessageFramer.TryReadFrame(
            result.FrameMemory,
            out _,
            out var messageType,
            out _,
            out _));
        Assert.Equal(MessageType.Response, messageType);
    }

    private sealed class NoopDispatcher(string serviceName) : IServiceDispatcher, INonStreamingServiceDispatcher
    {
        public string ServiceName { get; } = serviceName;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingDispatcherMap : IReadOnlyDictionary<string, IServiceDispatcher>
    {
        private readonly IServiceDispatcher _dispatcher;

        public CountingDispatcherMap(IServiceDispatcher dispatcher) => _dispatcher = dispatcher;

        public int TryGetValueCount { get; private set; }

        public IEnumerable<string> Keys
        {
            get
            {
                yield return _dispatcher.ServiceName;
            }
        }

        public IEnumerable<IServiceDispatcher> Values
        {
            get
            {
                yield return _dispatcher;
            }
        }

        public int Count => 1;

        public IServiceDispatcher this[string key] =>
            key == _dispatcher.ServiceName ? _dispatcher : throw new KeyNotFoundException(key);

        public bool ContainsKey(string key) => key == _dispatcher.ServiceName;

        public bool TryGetValue(string key, out IServiceDispatcher value)
        {
            TryGetValueCount++;
            if (key == _dispatcher.ServiceName)
            {
                value = _dispatcher;
                return true;
            }

            value = null!;
            return false;
        }

        public IEnumerator<KeyValuePair<string, IServiceDispatcher>> GetEnumerator()
        {
            yield return new KeyValuePair<string, IServiceDispatcher>(
                _dispatcher.ServiceName,
                _dispatcher);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
