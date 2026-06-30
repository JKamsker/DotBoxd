using System.Net;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.Transports.Tcp;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.TcpTransportCoverageTestHelpers;

namespace DotBoxD.Services.Tests.Coverage.Transport.Concurrency;

public sealed class ClientTransportConcurrentConnectTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RejectTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task NamedPipeConnectAsync_ConcurrentPendingConnects_FailsClosedAndDisposeCancelsOriginal()
    {
        var transport = new NamedPipeClientTransport(CreatePipeName());
        using var firstCleanup = new CancellationTokenSource();
        using var secondCleanup = new CancellationTokenSource();
        var firstConnect = transport.ConnectAsync(firstCleanup.Token);
        var secondConnect = transport.ConnectAsync(secondCleanup.Token);

        try
        {
            var secondException = await Record.ExceptionAsync(
                () => secondConnect.WaitAsync(RejectTimeout));
            var invalidOperation = Assert.IsType<InvalidOperationException>(secondException);
            Assert.Contains("Connect already in progress", invalidOperation.Message);

            await transport.DisposeAsync();
            var firstException = await Record.ExceptionAsync(
                () => firstConnect.WaitAsync(Timeout));
            Assert.True(
                firstException is OperationCanceledException or ObjectDisposedException,
                $"Expected dispose to cancel the original pending connect, got {firstException?.GetType().Name ?? "success"}.");
        }
        finally
        {
            firstCleanup.Cancel();
            secondCleanup.Cancel();
            await transport.DisposeAsync();
            await Record.ExceptionAsync(() => firstConnect.WaitAsync(Timeout));
            await Record.ExceptionAsync(() => secondConnect.WaitAsync(Timeout));
        }
    }

    [Fact]
    public async Task TcpConnectAsync_ConcurrentPendingConnects_FailsClosed()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var transport = new TcpTransport("127.0.0.1", port);
        transport._onConnectAttemptStartedForTest = async () =>
        {
            firstStarted.TrySetResult();
            await releaseFirst.Task.ConfigureAwait(false);
        };
        var acceptTask = server.AcceptAsync();
        var firstConnect = transport.ConnectAsync();

        try
        {
            await firstStarted.Task.WaitAsync(Timeout);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => transport.ConnectAsync().WaitAsync(RejectTimeout));
            Assert.Contains("Connect already in progress", ex.Message);

            releaseFirst.SetResult();
            await firstConnect.WaitAsync(Timeout);
            await using var accepted = await acceptTask.WaitAsync(Timeout);
            Assert.True(transport.IsConnected);
            Assert.NotNull(transport.Connection);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await Record.ExceptionAsync(() => firstConnect.WaitAsync(Timeout));
        }
    }

    private static string CreatePipeName() => "dotboxd-test-" + Guid.NewGuid().ToString("N");
}
