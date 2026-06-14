using System.Buffers;
using MessagePack;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Coverage;

public sealed class Round10_ProtocolRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void MessagePackDeserializer_RejectsTrailingBytesAfterValidValue()
    {
        var serializer = new MessagePackRpcSerializer();
        var payloadWithTrailingGarbage = new byte[] { 0x7b, 0xc1 };

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<int>(payloadWithTrailingGarbage.AsMemory()));
        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize(payloadWithTrailingGarbage.AsMemory(), typeof(int)));
    }

    [Fact]
    public async Task InvokeAsync_ErrorFrameWithSuccessfulEnvelope_FaultsAsProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer).Start();

        var call = peer.InvokeAsync<int, string>("Svc", "Op", request: 1);
        channel.Enqueue(BuildContradictoryErrorFrame(serializer, messageId: 1));

        var ex = await Assert.ThrowsAsync<ShaRpcProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("error response", ex.Message);
    }

    [Fact]
    public async Task SingleConnectionServer_StartAsync_WithPreCancelledToken_DoesNotStart()
    {
        await using var channel = new ScriptedConnection();
        await using var server = new SingleConnectionServerTransport(channel);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.StartAsync(cts.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.AcceptAsync());
    }

    private static Payload BuildContradictoryErrorFrame(MessagePackRpcSerializer serializer, int messageId)
    {
        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, "success-through-error-frame");

        return MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Error,
            new RpcResponse { MessageId = messageId, IsSuccess = true },
            payloadWriter.WrittenSpan);
    }
}
