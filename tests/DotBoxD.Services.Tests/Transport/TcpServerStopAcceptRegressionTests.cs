using System.Net;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class TcpServerStopAcceptRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AcceptAsync_WhenStopAsyncStopsPendingAccept_ThrowsOperationCanceled()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);

        var acceptTask = server.AcceptAsync();
        await server.StopAsync().WaitAsync(Timeout);

        var ex = await Record.ExceptionAsync(() => acceptTask.WaitAsync(Timeout));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.IsNotType<ObjectDisposedException>(ex);
    }
}
