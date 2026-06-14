using System.Reflection;
using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Server;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Shared;
using Xunit;

namespace ShaRPC.Tests.Cov.Round2Public;

/// <summary>
/// Round-2 behavioral coverage for the internal <c>RpcErrors</c> exception-to-wire mapping, reached
/// only through the public peer stack (the type itself is internal). Covers the not-found typed
/// mapping for a handler-thrown <see cref="ShaRpcNotFoundException"/>, the faulting-transformer
/// fallback (which reports to diagnostics and returns the opaque default), and the long-message
/// truncation. Each scenario drives a real client/server <see cref="RpcPeer"/> pair over an in-memory
/// pipe and asserts what the caller observes on the <see cref="ShaRpcRemoteException"/>.
/// </summary>
public sealed class CoreErrorMappingCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // RpcDiagnostics.Error is a process-wide static event; serialize the test that subscribes to it.
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = TimeSpan.FromSeconds(8) };

    // ----- Handler throws ShaRpcNotFoundException(Service) -> typed ServiceNotFound (RpcError 69) ---

    [Fact]
    public async Task HandlerThrowsNotFoundService_MapsToServiceNotFoundTypeAndKeepsMessage()
    {
        // A handler that throws ShaRpcNotFoundException with the default (Service) kind flows through
        // RpcErrors.FromException -> the not-found branch -> NotFoundErrorType default case. This is NOT
        // routed through the (absent) transformer, and the (short) message is preserved verbatim.
        const string detail = "no such widget";
        await using var pair = await PeerPair.StartAsync(
            server => server.Provide<IGameService>(
                new ThrowingGameService(() => new ShaRpcNotFoundException(detail))),
            serverOptions: null);

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
            () => pair.Game.GetServerStatusAsync().WaitAsync(Timeout));

        Assert.Equal(RpcErrorTypes.ServiceNotFound, ex.RemoteExceptionType);
        Assert.Equal(detail, ex.Message);
    }

    [Fact]
    public async Task HandlerThrowsNotFoundMethod_MapsToMethodNotFoundType()
    {
        // The Method kind exercises a non-default switch arm of NotFoundErrorType so the typed mapping
        // is observably kind-sensitive (and not just always-Service).
        await using var pair = await PeerPair.StartAsync(
            server => server.Provide<IGameService>(
                new ThrowingGameService(() => new ShaRpcNotFoundException(
                    "missing method", ShaRpcNotFoundException.NotFoundKind.Method))),
            serverOptions: null);

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
            () => pair.Game.GetServerStatusAsync().WaitAsync(Timeout));

        Assert.Equal(RpcErrorTypes.MethodNotFound, ex.RemoteExceptionType);
    }

    // ----- Faulting transformer -> opaque default + diagnostics report (RpcError 31-37) -----

    [Fact]
    public async Task FaultingTransformer_FallsBackToInternalError_AndReportsToDiagnostics()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            // The transformer itself throws. RpcErrors.FromException must catch that, report it to
            // diagnostics, and fall back to the opaque "Internal error." rather than letting the
            // transformer fault escape and replace a handled error with an unhandled one.
            var transformerFault = new InvalidOperationException("transformer-marker-" + Guid.NewGuid().ToString("N"));
            var reported = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, transformerFault))
                {
                    reported.TrySetResult(args);
                }
            }

            RpcDiagnostics.Error += Handler;
            try
            {
                var serverOptions = new RpcPeerOptions
                {
                    ExceptionTransformer = _ => throw transformerFault,
                };

                await using var pair = await PeerPair.StartAsync(
                    server => server.Provide<IGameService>(
                        new ThrowingGameService(() => new KeyNotFoundException("handler boom"))),
                    serverOptions);

                var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                    () => pair.Game.GetServerStatusAsync().WaitAsync(Timeout));

                // Caller sees the opaque default, never the transformer's own fault or the handler detail.
                Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
                Assert.Equal("Internal error.", ex.Message);

                // The transformer fault was surfaced to diagnostics with a descriptive operation.
                var args = await reported.Task.WaitAsync(Timeout);
                Assert.Same(transformerFault, args.Error);
                Assert.Contains("transformer", args.Operation, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                RpcDiagnostics.Error -= Handler;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    // ----- Long transformer message is truncated to the reflected-value cap (RpcError 79) -----

    [Fact]
    public async Task TransformerWithOverlongMessage_TruncatesToCapWithEllipsis()
    {
        // RpcErrors.Truncate caps reflected values at MaxReflectedValueLength (256) and appends "...".
        // A transformer returning a >256-char message exercises the truncation branch end-to-end.
        const int cap = 256;
        var longMessage = new string('M', 1000);

        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => new RpcErrorInfo(longMessage, "APP_LONG"),
        };

        await using var pair = await PeerPair.StartAsync(
            server => server.Provide<IGameService>(
                new ThrowingGameService(() => new InvalidOperationException("ignored"))),
            serverOptions);

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
            () => pair.Game.GetServerStatusAsync().WaitAsync(Timeout));

        Assert.Equal("APP_LONG", ex.RemoteExceptionType);
        Assert.Equal(cap, ex.Message.Length);
        Assert.EndsWith("...", ex.Message);
        // The first cap-3 characters are the original message content.
        Assert.StartsWith(new string('M', cap - 3), ex.Message);
    }

    // -----------------------------------------------------------------------------------------
    // Test plumbing
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A live client/server <see cref="RpcPeer"/> pair over an in-memory pipe. The server is configured
    /// by a callback (so each test injects its own service + options) and the client exposes the
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
    /// An <see cref="IGameService"/> whose <c>GetServerStatusAsync</c> throws an exception produced by
    /// the supplied factory, so the dispatch error path (and thus the RpcErrors mapping) runs for a
    /// real generated dispatcher.
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

/// <summary>
/// Round-2 coverage for <c>ShaRpcGeneratedAssemblyCatalog</c> sink registration over an assembly that
/// runs no ShaRPC generator: the sink registrar short-circuits to a no-op (returns <c>default</c>) so
/// the supplied sink receives nothing rather than faulting. Reached via the public
/// <see cref="ShaRpcServiceRegistry"/> sink overloads. The catalog type is internal; this exercises
/// the empty-assembly branch the round-1 suite (which only used the generated Shared assembly) missed.
/// </summary>
public sealed class CatalogEmptyAssemblyCoverageTests
{
    private static Assembly TestAssemblyWithoutGenerator => typeof(CatalogEmptyAssemblyCoverageTests).Assembly;

    [Fact]
    public void RegisterServices_AssemblyWithoutGeneratedType_LeavesSinkEmpty()
    {
        var sink = new RecordingServiceSink();

        // No generated factory type in the test assembly -> CreateSinkRegistrar returns a no-op
        // registrar -> the sink is never called.
        ShaRpcServiceRegistry.RegisterServices(new[] { TestAssemblyWithoutGenerator }, sink);

        Assert.Empty(sink.ServiceTypes);
    }

    [Fact]
    public void RegisterGeneratedServices_AssemblyWithoutGeneratedType_LeavesSinkEmpty()
    {
        var sink = new RecordingGeneratedSink();

        ShaRpcServiceRegistry.RegisterGeneratedServices(new[] { TestAssemblyWithoutGenerator }, sink);

        Assert.Empty(sink.ServiceTypes);
    }

    [Fact]
    public void RegisterServices_MixedAssemblies_OnlyGeneratedAssemblyContributes()
    {
        var sink = new RecordingServiceSink();

        // One generated assembly (Shared) + one without a generator (the test assembly). Only the
        // generated one feeds the sink; the empty one is a silent no-op.
        ShaRpcServiceRegistry.RegisterServices(
            new[] { typeof(IGameService).Assembly, TestAssemblyWithoutGenerator },
            sink);

        Assert.Contains(typeof(IGameService), sink.ServiceTypes);
    }

    private sealed class RecordingServiceSink : IShaRpcServiceRegistrationSink
    {
        public List<Type> ServiceTypes { get; } = new();

        public void AddService<TService, TImplementation>()
            where TService : class
            where TImplementation : TService =>
            ServiceTypes.Add(typeof(TService));
    }

    private sealed class RecordingGeneratedSink : IShaRpcGeneratedServiceRegistrationSink
    {
        public List<Type> ServiceTypes { get; } = new();

        public void AddService<TService, TProxy, TDispatcher>()
            where TService : class
            where TProxy : TService
            where TDispatcher : IServiceDispatcher =>
            ServiceTypes.Add(typeof(TService));
    }
}
