namespace DotBoxd.Kernels.Transport.Ipc;

using DotBoxd.Services;
using DotBoxd.Services.Transport;
using DotBoxd.Codecs.MessagePack;
using DotBoxd.Transports.NamedPipes;

public static class DotBoxdDotBoxdRpcMessagePackIpc
{
    private const int MinimumSafePipeNameLength = 32;
    private const int MinimumSafePipeNameDistinctCharacters = 8;
    private static readonly MessagePackRpcSerializer Serializer = new();
    // Get-only client default. DotBoxd.Kernels plugin IPC clients are pure callers, so this opts into the
    // DotBoxd low-allocation unary ValueTask<T> path: that pooled path is only taken when
    // RequestTimeout is Timeout.InfiniteTimeSpan AND EnableLowAllocationValueTaskInvocations is set
    // (and the caller passes no cancellable token). Keeping the convenience default on this profile
    // means the easy path is the measured low-allocation path (PAL-0014). Callers needing a per-call
    // timeout can pass their own RpcPeerOptions or a CancellationToken.
    private static readonly RpcPeerOptions DefaultClientOptions = new() {
        RequestTimeout = Timeout.InfiniteTimeSpan,
        EnableLowAllocationValueTaskInvocations = true,
        RejectInboundCalls = true
    };
    private static readonly RpcPeerOptions DefaultBidirectionalClientOptions = new() {
        RequestTimeout = TimeSpan.FromSeconds(10)
    };
    // Server-side convenience default. DotBoxd.Kernels plugin IPC hosts answer unary calls from trusted
    // clients, so this opts the convenience listen path into the matching DotBoxd low-allocation
    // profile: an infinite request timeout plus DisableInboundRequestCancellation so non-streaming
    // inbound calls do not allocate per-request cancellation state, and an unbounded inbound queue so
    // requests dispatch immediately without per-request queue bookkeeping. Keeping the convenience
    // default on this profile means the easy listen path is the measured low-allocation path
    // (PAL-0014). Hosts needing bounded backpressure or cancellable handlers can pass their own
    // RpcPeerOptions.
    private static readonly RpcPeerOptions DefaultServerOptions = new() {
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
        => ListenNamedPipe(pipeName, configurePeer, DotBoxdNamedPipeOptions.Default, options);

    public static RpcHost ListenNamedPipe(
        string pipeName,
        Action<RpcPeer> configurePeer,
        DotBoxdNamedPipeOptions namedPipeOptions,
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
        DotBoxdNamedPipeOptions namedPipeOptions,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(".", pipeName, namedPipeOptions, options, cancellationToken);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string serverName,
        string pipeName,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
        => ConnectNamedPipeAsync(serverName, pipeName, DotBoxdNamedPipeOptions.Default, options, cancellationToken);

    public static Task<RpcPeerSession> ConnectNamedPipeAsync(
        string serverName,
        string pipeName,
        DotBoxdNamedPipeOptions namedPipeOptions,
        RpcPeerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePipeName(pipeName, namedPipeOptions);
        return ConnectAsync(new NamedPipeClientTransport(serverName, pipeName), options, cancellationToken);
    }

    private static void ValidatePipeName(string pipeName, DotBoxdNamedPipeOptions options)
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

public sealed record DotBoxdNamedPipeOptions(bool AllowUnsafeLowEntropyName = false)
{
    public static DotBoxdNamedPipeOptions Default { get; } = new();
    public static DotBoxdNamedPipeOptions UnsafeDevelopment { get; } = new(AllowUnsafeLowEntropyName: true);
}
