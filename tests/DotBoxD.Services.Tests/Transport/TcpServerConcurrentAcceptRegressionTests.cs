using System.Net;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class TcpServerConcurrentAcceptRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AcceptAsync_ConcurrentCancelledCalls_StartOnlyOneFreshAccept()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        using var firstCancel = new CancellationTokenSource();
        using var secondCancel = new CancellationTokenSource();
        using var releaseFreshAccept = new ManualResetEventSlim();
        var firstFreshAcceptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var freshAcceptStarts = 0;
        server._onFreshAcceptStartedForTest = () =>
        {
            if (Interlocked.Increment(ref freshAcceptStarts) == 1)
            {
                firstFreshAcceptStarted.TrySetResult();
            }

            releaseFreshAccept.Wait(Timeout);
        };

        var first = Task.Run(() => server.AcceptAsync(firstCancel.Token));
        var second = Task.Run(() => server.AcceptAsync(secondCancel.Token));
        await firstFreshAcceptStarted.Task.WaitAsync(Timeout);
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        firstCancel.Cancel();
        secondCancel.Cancel();
        releaseFreshAccept.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(Timeout));
        Assert.Equal(1, server.FreshAcceptStartsForTest);
    }
}
