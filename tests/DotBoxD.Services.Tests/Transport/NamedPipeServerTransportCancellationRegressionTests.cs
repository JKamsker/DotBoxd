using DotBoxD.Transports.NamedPipes;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class NamedPipeServerTransportCancellationRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PreCancelledAcceptBeforeStart_ThrowsCancellation()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AcceptAsync(cancelledCts.Token).WaitAsync(Timeout));
    }

    [Fact]
    public async Task PreCancelledAccept_DoesNotDisturbPendingAccept()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());
        await server.StartAsync();
        var pendingAccept = server.AcceptAsync();

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AcceptAsync(cancelledCts.Token).WaitAsync(Timeout));

        await Assert.ThrowsAsync<TimeoutException>(
            () => pendingAccept.WaitAsync(TimeSpan.FromMilliseconds(500)));

        await server.StopAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pendingAccept.WaitAsync(Timeout));
    }

    private static string CreatePipeName() => "dotboxd-test-" + Guid.NewGuid().ToString("N");
}
