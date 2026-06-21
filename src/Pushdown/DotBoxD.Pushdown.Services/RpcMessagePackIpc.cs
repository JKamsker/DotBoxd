using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.NamedPipes;

namespace DotBoxD.Pushdown.Services;

public static class RpcMessagePackIpc
{
    private const int MinimumSafePipeNameLength = 32;
    private const int MinimumSafePipeNameDistinctCharacters = 8;
    private static readonly MessagePackRpcSerializer Serializer = new();
    // Get-only client default. DotBoxD.Kernels plugin IPC clients are pure callers, so this opts into the
    // DotBoxD low-allocation unary ValueTask<T> path: that pooled path is only taken when
    // RequestTimeout is Timeout.InfiniteTimeSpan AND EnableLowAllocationValueTaskInvocations is set
    // (and the caller passes no cancellable token). Keeping the convenience default on this profile
    // means the easy path is the measured low-allocation path (PAL-0014). Callers needing a per-call
    // timeout can pass their own RpcPeerOptions or a CancellationToken.
    private static readonly RpcPeerOptions DefaultClientOptions = new()
    {
        RequestTimeout = Timeout.InfiniteTimeSpan,
        EnableLowAllocationValueTaskInvocations = true,
        RejectInboundCalls = true
    };
    private static readonly RpcPeerOptions DefaultBidirectionalClientOptions = new()
    {
        RequestTimeout = TimeSpan.FromSeconds(10)
    };
    // Server-side convenience default. DotBoxD.Kernels plugin IPC hosts answer unary calls from trusted
    // clients, so this opts the convenience listen path into the matching DotBoxD low-allocation
    // profile: an infinite request timeout plus DisableInboundRequestCancellation so non-streaming
    // inbound calls do not allocate per-request cancellation state, and an unbounded inbound queue so
    // requests dispatch immediately without per-request queue bookkeeping. Keeping the convenience
    // default on this profile means the easy listen path is the measured low-allocation path
    // (PAL-0014). Hosts needing bounded backpressure or cancellable handlers can pass their own
    // RpcPeerOptions.
    private static readonly RpcPeerOptions DefaultServerOptions = new()
    {
        RequestTimeout = Timeout.InfiniteTimeSpan,
        DisableInboundRequestCancellation = true,
        InboundQueueCapacity = null
    };

    public static RpcHost Listen(
        IServerTransport transport,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(configurePeer);
        return RpcHost.Listen(transport, Serializer, options ?? DefaultServerOptions).ForEachPeer(configurePeer);
    }

    public static Task<RpcPeerSession> ConnectAsync(
        ITransport transport,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return transport.ConnectPeerAsync(Serializer, options ?? DefaultClientOptions, cancellationToken);
    }

    public static Task<RpcPeerSession> ConnectAsync(
        ITransport transport,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(configurePeer);
        return transport.ConnectPeerAsync(
            Serializer,
            configurePeer,
            options ?? DefaultBidirectionalClientOptions,
            cancellationToken);
    }

    public static RpcHost ListenNamedPipe(
        string pipeName,
        Action<RpcPeer> configurePeer,
        RpcPeerOptions? options = null)
        => ListenNamedPipe(pipeName, configurePeer, NamedPipeTransportOptions.Default, options);

    public static RpcHost ListenNamedPipe(
        string pipeName,
        Action<RpcPeer> configurePeer,
        NamedPipeTransportOptions namedPipeOptions,
        RpcPeerOptions? options = null)
    {
        ValidatePipeName(pipeName, namedPipeOptions);
        return Listen(new NamedPipeServerTransport(pipeName), configurePeer, options);
    }

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(".", pipeName, options, cancellationToken);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string pipeName,
        NamedPipeTransportOptions namedPipeOptions,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(".", pipeName, namedPipeOptions, options, cancellationToken);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string serverName,
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(serverName, pipeName, NamedPipeTransportOptions.Default, options, cancellationToken);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string serverName,
        string pipeName,
        NamedPipeTransportOptions namedPipeOptions,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePipeName(pipeName, namedPipeOptions);
        return ConnectAsync(new NamedPipeClientTransport(serverName, pipeName), options, cancellationToken);
    }

    /// <summary>
    /// Connects to a named-pipe server and, when <paramref name="configurePeer"/> is supplied, registers
    /// bidirectional services on the peer before it starts — the only point at which a client may provide a
    /// reverse callback (e.g. a remote <c>RunLocal</c> event sink). A <c>null</c> callback connects without
    /// registering any client-side service. The pipe name is validated as in the other overloads.
    /// </summary>
    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string pipeName,
        Action<RpcPeer>? configurePeer,
        NamedPipeTransportOptions? namedPipeOptions = null,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var pipeOptions = namedPipeOptions ?? NamedPipeTransportOptions.Default;
        ValidatePipeName(pipeName, pipeOptions);
        var transport = new NamedPipeClientTransport(".", pipeName);
        return configurePeer is null
            ? ConnectAsync(transport, options, cancellationToken)
            : ConnectAsync(transport, configurePeer, options, cancellationToken);
    }

    private static void ValidatePipeName(string pipeName, NamedPipeTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Any(char.IsControl))
        {
            throw new ArgumentException("named pipe name must be non-empty and must not contain control characters", nameof(pipeName));
        }

        if (options.AllowUnsafeLowEntropyName)
        {
            return;
        }

        if (pipeName.Length < MinimumSafePipeNameLength ||
            pipeName.Distinct().Count() < MinimumSafePipeNameDistinctCharacters)
        {
            throw new ArgumentException(
                "named pipe name must include an unguessable 128-bit random component or explicitly opt into unsafe development names",
                nameof(pipeName));
        }
    }
}

public sealed record NamedPipeTransportOptions(bool AllowUnsafeLowEntropyName = false)
{
    public static NamedPipeTransportOptions Default { get; } = new();
    public static NamedPipeTransportOptions UnsafeDevelopment { get; } = new(AllowUnsafeLowEntropyName: true);
}
