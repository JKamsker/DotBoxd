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
}
