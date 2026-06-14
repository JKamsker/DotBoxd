using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Shared;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round-2 regression for DEFECT #6: <c>RpcErrors.FromException</c> truncates the transformer-supplied
/// <c>info.Message</c> to <c>MaxReflectedValueLength</c> but assigns <c>info.Type</c> unbounded. A custom
/// <see cref="RpcPeerOptions.ExceptionTransformer"/> returning a huge <c>Type</c> string therefore sends
/// it on the wire untruncated, inconsistent with the message cap. The desired behaviour is that the
/// reflected exception type is bounded by the same <c>MaxReflectedValueLength</c> cap (with an ellipsis),
/// exactly like the message path. This test asserts that desired behaviour, so it is RED on the current
/// (unfixed) code where <c>RemoteExceptionType.Length</c> arrives at the full 1000 characters.
/// </summary>
public sealed class Round2_ErrorTypeTruncationTests
{
    // RpcErrors.MaxReflectedValueLength is internal (256). Mirror the existing transformer-truncation
    // test (CoreErrorMappingCoverageTests.TransformerWithOverlongMessage_TruncatesToCapWithEllipsis),
    // which hardcodes the same cap, since the constant is not publicly reachable.
    private const int Cap = 256;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = TimeSpan.FromSeconds(8) };

    [Fact]
    public async Task TransformerWithOverlongType_TruncatesToCapWithEllipsis()
    {
        // The transformer returns a short message but a >256-char Type. The message path is already
        // truncated; the Type path must be bounded by the same cap. On the unfixed code the Type is
        // assigned verbatim, so RemoteExceptionType.Length == 1000 here -> RED. Once Truncate is applied
        // to info.Type, the type arrives capped at 256 chars ending in "..." -> GREEN.
        var longType = new string('T', 1000);

        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => new RpcErrorInfo("short", longType),
        };

        await using var pair = await PeerPair.StartAsync(
            server => server.Provide<IGameService>(
                new ThrowingGameService(() => new InvalidOperationException("ignored"))),
            serverOptions);

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
            () => pair.Game.GetServerStatusAsync().WaitAsync(Timeout));

        // The (short) message is unaffected; only the type is the subject under test.
        Assert.Equal("short", ex.Message);

        // The reflected type must be bounded by the same reflected-value cap as the message, with the
        // ellipsis marker, and must carry the original content up to the cap.
        Assert.True(
            ex.RemoteExceptionType.Length <= Cap,
            $"Expected RemoteExceptionType length <= {Cap} but was {ex.RemoteExceptionType.Length}.");
        Assert.Equal(Cap, ex.RemoteExceptionType.Length);
        Assert.EndsWith("...", ex.RemoteExceptionType);
        Assert.StartsWith(new string('T', Cap - 3), ex.RemoteExceptionType);
    }

    // -----------------------------------------------------------------------------------------
    // Test plumbing (mirrors CoreErrorMappingCoverageTests.PeerPair / ThrowingGameService).
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A live client/server <see cref="RpcPeer"/> pair over an in-memory pipe. The server is configured
    /// by a callback (so the test injects its own service + options) and the client exposes the
    /// generated <see cref="IGameService"/> proxy.
    /// </summary>
    private sealed class PeerPair : IAsyncDisposable
    {
        private readonly RpcPeer _server;
        private readonly RpcPeer _client;

        public IGameService Game { get; }

        private PeerPair(RpcPeer server, RpcPeer client, IGameService game)
        {
            _server = server;
            _client = client;
            Game = game;
        }

        public static Task<PeerPair> StartAsync(Action<RpcPeer> configureServer, RpcPeerOptions? serverOptions)
        {
            var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
            var server = RpcPeer.Over(serverConnection, NewSerializer(), serverOptions ?? new RpcPeerOptions());
            configureServer(server);
            server.Start();

            var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
            return Task.FromResult(new PeerPair(server, client, client.GetGameService()));
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _server.DisposeAsync();
        }
    }

    /// <summary>
    /// An <see cref="IGameService"/> whose every method throws an exception produced by the supplied
    /// factory, so the dispatch error path (and thus the RpcErrors mapping) runs for a real generated
    /// dispatcher.
    /// </summary>
    private sealed class ThrowingGameService : IGameService
    {
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingGameService(Func<Exception> exceptionFactory) => _exceptionFactory = exceptionFactory;

        public Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default) =>
            throw _exceptionFactory();

        public Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default) =>
            throw _exceptionFactory();

        public Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default) =>
            throw _exceptionFactory();

        public Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default) =>
            throw _exceptionFactory();

        public Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default) =>
            throw _exceptionFactory();
    }
}
