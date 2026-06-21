using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

public sealed class RpcPeerProxyCacheTests
{
    [Fact]
    public async Task Get_RepeatedCalls_ReturnsSameRootProxy()
    {
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());

        var first = peer.Get<IGameService>();
        var second = peer.Get<IGameService>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GeneratedGetExtension_UsesCachedRootProxy()
    {
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());

        var direct = peer.Get<IGameService>();
        var extension = peer.GetGameService();

        Assert.Same(direct, extension);
        Assert.Same(extension, peer.GetGameService());
    }

    [Fact]
    public async Task Get_AfterDispose_ThrowsEvenWhenProxyWasCached()
    {
        await using var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());
        _ = peer.Get<IGameService>();

        await peer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => peer.Get<IGameService>());
    }

    [Fact]
    public async Task Get_AfterRegistryReplacement_RefreshesCachedProxy()
    {
        RegisterReplacementProxy(_ => new ReplacementProxyV1());
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());

        var first = peer.Get<IReplacementService>();
        RegisterReplacementProxy(_ => new ReplacementProxyV2());
        var second = peer.Get<IReplacementService>();

        Assert.IsType<ReplacementProxyV1>(first);
        Assert.IsType<ReplacementProxyV2>(second);
        Assert.NotSame(first, second);
    }

    private static void RegisterReplacementProxy(Func<IRpcInvoker, IReplacementService> proxyFactory) =>
        GeneratedServiceRegistry.Register(
            proxyFactory,
            _ => new ReplacementDispatcher(),
            new GeneratedService(
                typeof(IReplacementService),
                typeof(IReplacementService),
                typeof(ReplacementDispatcher),
                nameof(IReplacementService)));

    public interface IReplacementService
    {
        Task PingAsync(CancellationToken ct = default);
    }

    private sealed class ReplacementProxyV1 : IReplacementService
    {
        public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ReplacementProxyV2 : IReplacementService
    {
        public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ReplacementDispatcher : IServiceDispatcher
    {
        public string ServiceName => nameof(IReplacementService);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
