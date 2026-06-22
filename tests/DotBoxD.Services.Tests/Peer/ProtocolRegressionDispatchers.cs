using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Peer;

internal sealed class ThrowingDispatcher : IServiceDispatcher
{
    public const string Service = "Throwing";

    public string ServiceName => Service;

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) =>
        throw new InvalidOperationException("Internal path C:\\secret\\db.txt");
}

internal sealed class CancellableDispatcher : IServiceDispatcher
{
    public const string Service = "Cancellable";

    private readonly TaskCompletionSource<bool> _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _cancelled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ServiceName => Service;

    public Task Entered => _entered.Task;

    public Task Cancelled => _cancelled.Task;

    public async Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        _entered.TrySetResult(true);
        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _cancelled.TrySetResult(true);
            throw;
        }
    }
}

internal sealed class PingDispatcher : IServiceDispatcher
{
    public const string Service = "Ping";

    public string ServiceName => Service;

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        serializer.Serialize(output, 1);
        return Task.CompletedTask;
    }
}
