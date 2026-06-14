using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Shared;
using Xunit;

namespace ShaRPC.Tests.Cov.Round2Public;

/// <summary>
/// Round-2 deterministic coverage for the public <see cref="RpcPeer"/> guard/validation/metadata
/// surface that the round-1 suites left uncovered: the static <c>Over</c> argument guards, the
/// <c>RemoteEndpoint</c> projection, every branch of the three <c>Provide</c> overloads (including the
/// <c>ServiceProvider</c> resolution paths and the disposed guard), and the <c>Get&lt;T&gt;</c>
/// disposed guard plus live-proxy path. Everything is exercised through the public API only — no
/// reflection into privates — using the shared <see cref="ScriptedConnection"/> /
/// <see cref="InMemoryPipe"/> helpers and the MessagePack serializer.
/// </summary>
public sealed class RpcPeerPublicGuardCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static RpcPeerOptions Options() => new() { RequestTimeout = Timeout };

    // ----- Over(...) argument guards (RpcPeer 57-58, 62-63) -----

    [Fact]
    public void Over_NullChannel_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcPeer.Over(null!, NewSerializer()));
        Assert.Equal("channel", ex.ParamName);
    }

    [Fact]
    public async Task Over_NullSerializer_ThrowsArgumentNullException()
    {
        await using var channel = new ScriptedConnection();
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcPeer.Over(channel, null!));
        Assert.Equal("serializer", ex.ParamName);
    }

    // ----- RemoteEndpoint projection (RpcPeer 76) -----

    [Fact]
    public async Task RemoteEndpoint_ReturnsUnderlyingChannelEndpoint()
    {
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, NewSerializer(), Options());

        // ScriptedConnection.RemoteEndpoint is the fixed "scripted://remote".
        Assert.Equal(channel.RemoteEndpoint, peer.RemoteEndpoint);
        Assert.Equal("scripted://remote", peer.RemoteEndpoint);
    }

    // ----- Provide<TService>(implementation) null guard (RpcPeer 104-105) -----

    [Fact]
    public async Task ProvideImplementation_Null_ThrowsArgumentNullException()
    {
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, NewSerializer(), Options());

        var ex = Assert.Throws<ArgumentNullException>(() => peer.Provide<IGameService>(null!));
        Assert.Equal("implementation", ex.ParamName);
    }

    // ----- Provide<TService>() from ServiceProvider (RpcPeer 116-117, 121-122) -----

    [Fact]
    public async Task ProvideFromProvider_NoServiceProviderConfigured_ThrowsInvalidOperation()
    {
        await using var channel = new ScriptedConnection();
        // No ServiceProvider on the options -> _serviceProvider is null.
        await using var peer = RpcPeer.Over(channel, NewSerializer(), Options());

        var ex = Assert.Throws<InvalidOperationException>(() => peer.Provide<IGameService>());
        Assert.Contains("No ServiceProvider configured", ex.Message);
    }

    [Fact]
    public async Task ProvideFromProvider_ProviderReturnsNull_ThrowsInvalidOperation()
    {
        await using var channel = new ScriptedConnection();
        // The provider resolves null for IGameService -> the "did not resolve" guard fires.
        var options = new RpcPeerOptions
        {
            RequestTimeout = Timeout,
            ServiceProvider = new StubServiceProvider(_ => null),
        };
        await using var peer = RpcPeer.Over(channel, NewSerializer(), options);

        var ex = Assert.Throws<InvalidOperationException>(() => peer.Provide<IGameService>());
        Assert.Contains("did not resolve", ex.Message);
        Assert.Contains(typeof(IGameService).FullName!, ex.Message);
    }

    [Fact]
    public async Task ProvideFromProvider_ProviderResolvesService_RegistersAndReturnsSamePeer()
    {
        await using var channel = new ScriptedConnection();
        var implementation = new TestGameService();
        var options = new RpcPeerOptions
        {
            RequestTimeout = Timeout,
            ServiceProvider = new StubServiceProvider(
                type => type == typeof(IGameService) ? implementation : null),
        };
        await using var peer = RpcPeer.Over(channel, NewSerializer(), options);

        // Resolves IGameService from the provider, builds a dispatcher, and returns the peer fluently.
        var returned = peer.Provide<IGameService>();

        Assert.Same(peer, returned);
    }

    // ----- Provide(IServiceDispatcher) null guard (RpcPeer 132-133) -----

    [Fact]
    public async Task ProvideDispatcher_Null_ThrowsArgumentNullException()
    {
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, NewSerializer(), Options());

        var ex = Assert.Throws<ArgumentNullException>(() => peer.Provide((IServiceDispatcher)null!));
        Assert.Equal("dispatcher", ex.ParamName);
    }

    // ----- Provide after dispose (RpcPeer 139-140) -----

    [Fact]
    public async Task ProvideDispatcher_AfterDispose_ThrowsObjectDisposed()
    {
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, NewSerializer(), Options());
        await peer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => peer.Provide((IServiceDispatcher)new NoopDispatcher()));

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task ProvideImplementation_AfterDispose_ThrowsObjectDisposed()
    {
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, NewSerializer(), Options());
        await peer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => peer.Provide<IGameService>(new TestGameService()));

        await channel.DisposeAsync();
    }

    // ----- Provide on a peer whose connection has closed pre-dispose (RpcPeer 144-145) -----

    [Fact]
    public async Task ProvideDispatcher_AfterRemoteClose_ThrowsConnectionClosed()
    {
        // Drive the closed-but-not-disposed branch: an empty inbound frame makes the read loop run
        // its remote-close teardown, which marks the peer closed (MarkClosed) without disposing it.
        // A subsequent Provide must hit the _closed guard (ShaRpcConnectionException) rather than the
        // disposed guard.
        await using var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, NewSerializer(), Options());
        peer.Start();

        channel.Enqueue(ShaRPC.Core.Buffers.Payload.Empty);

        // Poll for the closed projection to settle (it is flipped on the read-loop thread) without a
        // fixed sleep, then assert Provide is rejected with the closed-connection error.
        await WaitUntilAsync(() => !peer.IsConnected, Timeout);

        var ex = Assert.Throws<ShaRpcConnectionException>(
            () => peer.Provide((IServiceDispatcher)new NoopDispatcher()));
        Assert.Contains("closed", ex.Message);

        await peer.DisposeAsync();
    }

    // ----- Get<TService> guards / live proxy (RpcPeer 171, 173-174) -----

    [Fact]
    public async Task Get_AfterDispose_ThrowsObjectDisposed()
    {
        var channel = new ScriptedConnection();
        var peer = RpcPeer.Over(channel, NewSerializer(), Options());
        await peer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => peer.Get<IGameService>());

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task Get_OnLivePeer_ReturnsWorkingProxyRoutedThroughThePeer()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        // Get returns a generated proxy bound to this peer (CreateProxy<TService>(this)).
        var game = peer.Get<IGameService>();
        Assert.NotNull(game);
        Assert.IsAssignableFrom<IGameService>(game);

        // Prove the proxy actually drives the peer's outbound path: the first call gets message id 1,
        // and a scripted response frame correlated to id 1 completes it with the deserialized result.
        var call = game.GetServerStatusAsync();
        channel.Enqueue(ServerStatusResponseFrame(serializer, messageId: 1, version: "live-proxy"));

        var status = await call.WaitAsync(Timeout);
        Assert.Equal("live-proxy", status.Version);
    }

    // ----- Normal start+provide+dispose teardown (RpcPeer 278-279, 281 DisposeCoreAsync) -----

    [Fact]
    public async Task Dispose_StartedPeerWithProvidedServiceAndInFlightCall_TearsDownCleanly()
    {
        // A started peer that has provided a service AND has an in-flight outbound call exercises the
        // full DisposeCoreAsync teardown: read-loop await (catch block 278-281), StopCancelFramesAsync,
        // FailPending, and inbound StopAsync. The in-flight call must fault with Connection closed.
        var serializer = NewSerializer();
        var channel = new ScriptedConnection();
        var peer = RpcPeer
            .Over(channel, serializer, Options())
            .Provide<IGameService>(new TestGameService());
        peer.Start();

        // No response is ever queued, so this call is still pending when dispose runs.
        var inFlight = peer.InvokeAsync<string, ServerStatus>("IGameService", "GetServerStatusAsync", "x");

        await peer.DisposeAsync().AsTask().WaitAsync(Timeout);

        var ex = await Assert.ThrowsAsync<ShaRpcConnectionException>(() => inFlight.WaitAsync(Timeout));
        Assert.Contains("closed", ex.Message);
        Assert.False(peer.IsConnected);

        await channel.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not satisfied within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static ShaRPC.Core.Buffers.Payload ServerStatusResponseFrame(
        ISerializer serializer, int messageId, string version)
    {
        var status = new ServerStatus { PlayerCount = 0, ServerTime = "now", Version = version };
        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, status);

        return ShaRPC.Core.Protocol.MessageFramer.FrameMessage(
            serializer,
            messageId,
            ShaRPC.Core.Protocol.MessageType.Response,
            new ShaRPC.Core.Protocol.RpcResponse { MessageId = messageId, IsSuccess = true },
            payloadWriter.WrittenSpan);
    }

    /// <summary>Minimal <see cref="IServiceProvider"/> that delegates resolution to a callback.</summary>
    private sealed class StubServiceProvider : IServiceProvider
    {
        private readonly Func<Type, object?> _resolve;

        public StubServiceProvider(Func<Type, object?> resolve) => _resolve = resolve;

        public object? GetService(Type serviceType) => _resolve(serviceType);
    }

    /// <summary>A no-op dispatcher used only to drive the <c>Provide(dispatcher)</c> guard paths.</summary>
    private sealed class NoopDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Noop";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
