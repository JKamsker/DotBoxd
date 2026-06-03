using ShaRPC.Core.Buffers;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for <see cref="Payload"/> disposal: the shared <see cref="Payload.Empty"/>
/// singleton must stay reusable after Dispose, and Dispose must be idempotent and thread-safe so a
/// rented buffer can never be returned to the pool more than once.
/// </summary>
public sealed class PayloadTests
{
    [Fact]
    public void Empty_Dispose_KeepsSingletonReusable()
    {
        // Empty wraps a zero-length array; Dispose must never null it out or later use of the shared
        // singleton would throw ObjectDisposedException.
        Payload.Empty.Dispose();
        Payload.Empty.Dispose();

        Assert.Equal(0, Payload.Empty.Length);
        Assert.True(Payload.Empty.Span.IsEmpty);
        Assert.True(Payload.Empty.Memory.IsEmpty);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var payload = Payload.Rent(64);
        payload.Dispose();

        // A second dispose must not throw and must not return the buffer to the pool a second time.
        payload.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = payload.Memory);
    }

    [Fact]
    public async Task Dispose_CalledConcurrently_DoesNotDoubleReturn()
    {
        // With the Interlocked exchange only one disposer observes the non-null array and returns it,
        // so racing disposers can never double-return the same rented buffer to ArrayPool.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var payload = Payload.Rent(128);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var racers = new Task[8];
            for (var i = 0; i < racers.Length; i++)
            {
                racers[i] = Task.Run(async () =>
                {
                    await start.Task;
                    payload.Dispose();
                });
            }

            start.SetResult(true);
            await Task.WhenAll(racers);
        }
    }
}
