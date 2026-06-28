using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class SingleConnectionServerTransportLifecycleRegressionTests
{
    [Fact]
    public async Task StopAsyncBeforeFirstAccept_PreventsAccept()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        await using var server = new SingleConnectionServerTransport(connection);

        await server.StartAsync();
        await server.StopAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
    }

    [Fact]
    public async Task CancelledPendingAccept_DoesNotCancelOtherPendingOrFutureAccepts()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        await using var server = new SingleConnectionServerTransport(connection);
        await server.StartAsync();
        _ = await server.AcceptAsync();

        var unaffectedAccept = server.AcceptAsync();

        using var cancelledCts = new CancellationTokenSource();
        var cancelledAccept = server.AcceptAsync(cancelledCts.Token);
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledAccept);

        await Assert.ThrowsAsync<TimeoutException>(
            () => unaffectedAccept.WaitAsync(TimeSpan.FromMilliseconds(500)));

        var futureAccept = server.AcceptAsync();
        await Assert.ThrowsAsync<TimeoutException>(
            () => futureAccept.WaitAsync(TimeSpan.FromMilliseconds(500)));

        await server.StopAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => unaffectedAccept);
        await Assert.ThrowsAsync<OperationCanceledException>(() => futureAccept);
    }
}
