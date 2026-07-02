using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class StreamConnectionCloseCancellationRegressionTests
{
    [Fact]
    public async Task CloseAsync_WithPreCanceledToken_DoesNotDisposeOwnedStream()
    {
        var stream = new TrackingDisposeStream();
        var connection = new StreamConnection(stream, ownsStream: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.CloseAsync(cts.Token));

        Assert.Equal(0, stream.DisposeCount);
        Assert.True(connection.IsConnected);

        await connection.DisposeAsync();

        Assert.Equal(1, stream.DisposeCount);
        Assert.False(connection.IsConnected);
    }
}
