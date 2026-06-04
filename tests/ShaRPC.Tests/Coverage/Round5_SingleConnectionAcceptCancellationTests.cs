using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 5 regression for <see cref="SingleConnectionServerTransport.AcceptAsync"/>. When
/// <c>StopAsync</c> released a parked second accept, it threw the zero-argument
/// <see cref="OperationCanceledException"/>, whose <see cref="OperationCanceledException.CancellationToken"/>
/// is <see cref="CancellationToken.None"/>. Standard token-scoped catch filters
/// (<c>catch (OperationCanceledException e) when (e.CancellationToken == myToken)</c>) silently miss it.
/// <c>TcpServerTransport</c> and <c>NamedPipeServerTransport</c> both throw <c>new
/// OperationCanceledException(ct)</c>; this transport must match that contract.
/// </summary>
public sealed class Round5_SingleConnectionAcceptCancellationTests
{
    [Fact]
    public async Task AcceptAsync_WhenStoppedWhilePending_ThrowsOceCarryingTheCallerToken()
    {
        await using var transport = new SingleConnectionServerTransport(new StubChannel());
        await transport.StartAsync();
        await transport.AcceptAsync(); // first accept returns the single connection

        using var cts = new CancellationTokenSource();
        var pending = transport.AcceptAsync(cts.Token); // parks on the stop signal; token NOT cancelled

        await transport.StopAsync();

        var oce = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(cts.Token, oce.CancellationToken);
    }

    private sealed class StubChannel : IRpcChannel
    {
        public bool IsConnected => true;

        public string RemoteEndpoint => "stub://remote";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) => Task.FromResult(Payload.Empty);

        public ValueTask DisposeAsync() => default;
    }
}
