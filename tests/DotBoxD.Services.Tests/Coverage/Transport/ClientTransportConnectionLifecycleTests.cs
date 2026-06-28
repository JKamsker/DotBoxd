using System.Net;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.Transports.Tcp;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.TcpTransportCoverageTestHelpers;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class ClientTransportConnectionLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TcpTransport_Connection_BecomesNull_AfterDispose()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        var transport = new TcpTransport("127.0.0.1", port);
        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync().WaitAsync(Timeout);
        await using var accepted = await acceptTask.WaitAsync(Timeout);

        Assert.NotNull(transport.Connection);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }

    [Fact]
    public async Task NamedPipeClientTransport_Connection_BecomesNull_AfterDispose()
    {
        var pipeName = "dotboxd-transport-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerTransport(pipeName);
        await server.StartAsync().WaitAsync(Timeout);

        var transport = new NamedPipeClientTransport(pipeName);
        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync().WaitAsync(Timeout);
        await using var accepted = await acceptTask.WaitAsync(Timeout);

        Assert.NotNull(transport.Connection);

        await transport.DisposeAsync();

        Assert.False(transport.IsConnected);
        Assert.Null(transport.Connection);
    }
}
