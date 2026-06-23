using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

internal static class PeerInboundQueueCoverageTestSupport
{
    public static MessagePackRpcSerializer NewSerializer() => new();

    public static async Task SendRequestAsync(
        IRpcChannel channel, ISerializer serializer, int messageId, string service, string method)
    {
        using var frame = CreateRequestFrame(serializer, messageId, service, method);
        await channel.SendAsync(frame.Memory).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads frames until it observes a QueueFull error, bounded so regressions fail fast.
    /// </summary>
    public static async Task<DecodedError> ReadFirstQueueFullAsync(
        IRpcChannel channel, ISerializer serializer, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var frame = await channel.ReceiveAsync().WaitAsync(timeout);
            if (frame.Length == 0)
            {
                break;
            }

            if (!MessageFramer.TryReadFrame(
                frame.Memory, out var messageId, out var messageType, out var envelope, out _))
            {
                continue;
            }

            if (messageType != MessageType.Error)
            {
                continue;
            }

            var response = serializer.Deserialize<RpcResponse>(envelope);
            if (response.ErrorType == RpcErrorTypes.QueueFull)
            {
                return new DecodedError(messageId, response.ErrorType, response.ErrorMessage);
            }
        }

        throw new TimeoutException("No QueueFull error frame was observed.");
    }

    public static Payload CreateRequestFrame(
        ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);
}

internal readonly record struct DecodedError(int MessageId, string? ErrorType, string? ErrorMessage);

internal sealed class EchoNumberDispatcher : IServiceDispatcher
{
    public const string Service = "EchoNumber";

    public string ServiceName => Service;

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        var value = serializer.Deserialize<int>(payload);
        serializer.Serialize(output, value);
        return Task.CompletedTask;
    }
}

internal sealed class BlockingDispatcher : IServiceDispatcher
{
    public const string Service = "Blocking";

    private readonly TaskCompletionSource<bool> _firstEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ServiceName => Service;

    public Task FirstEntered => _firstEntered.Task;

    public async Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        _firstEntered.TrySetResult(true);
        await _release.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public void Release() => _release.TrySetResult(true);
}

/// <summary>
/// A serial dispatcher that counts dispatches and signals once a target count has been reached.
/// </summary>
internal sealed class CountingBlockingDispatcher : IServiceDispatcher
{
    public const string Service = "Counting";

    private readonly int _unblockAfter;
    private readonly TaskCompletionSource<bool> _allDispatched =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _count;

    public CountingBlockingDispatcher(int unblockAfter) => _unblockAfter = unblockAfter;

    public string ServiceName => Service;

    public Task AllDispatched => _allDispatched.Task;

    public int DispatchedCount => Volatile.Read(ref _count);

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _count) >= _unblockAfter)
        {
            _allDispatched.TrySetResult(true);
        }

        return Task.CompletedTask;
    }
}
