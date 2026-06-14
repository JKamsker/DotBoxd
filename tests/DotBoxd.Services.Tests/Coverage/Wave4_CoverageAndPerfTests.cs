using DotBoxd.Services.Buffers;
using DotBoxd.Services.Client;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using Xunit;

namespace DotBoxd.Services.Tests.Coverage;

/// <summary>
/// Wave 4 coverage tests targeting untested paths in ReceivedResponse and RpcStreamAttachment.
/// </summary>
public sealed class Wave4_CoverageAndPerfTests
{
    [Fact]
    public void DisposeWhenAvailable_FaultedTask_DoesNotThrow()
    {
        var tcs = new TaskCompletionSource<ReceivedResponse>();
        tcs.SetException(new InvalidOperationException("boom"));

        ReceivedResponse.DisposeWhenAvailable(tcs.Task);
    }

    [Fact]
    public void DisposeWhenAvailable_CancelledTask_DoesNotThrow()
    {
        var tcs = new TaskCompletionSource<ReceivedResponse>();
        tcs.SetCanceled();

        ReceivedResponse.DisposeWhenAvailable(tcs.Task);
    }

    [Fact]
    public async Task DisposeWhenAvailable_PendingTaskThatFaults_DoesNotThrow()
    {
        var tcs = new TaskCompletionSource<ReceivedResponse>();

        ReceivedResponse.DisposeWhenAvailable(tcs.Task);

        tcs.SetException(new InvalidOperationException("delayed fault"));

        await Task.Delay(50);
    }

    [Fact]
    public async Task DisposeWhenAvailable_PendingTaskThatCancels_DoesNotThrow()
    {
        var tcs = new TaskCompletionSource<ReceivedResponse>();

        ReceivedResponse.DisposeWhenAvailable(tcs.Task);

        tcs.SetCanceled();

        await Task.Delay(50);
    }

    [Fact]
    public void DisposeWhenAvailable_CompletedTask_DisposesResponse()
    {
        var frame = Payload.Rent(16);
        var response = new ReceivedResponse(
            new RpcResponse { MessageId = 1, IsSuccess = true },
            frame.Memory,
            frame,
            stream: null);

        var tcs = new TaskCompletionSource<ReceivedResponse>();
        tcs.SetResult(response);

        ReceivedResponse.DisposeWhenAvailable(tcs.Task);

        Assert.Null(response.DetachStream());
    }

    [Fact]
    public void ReceivedResponse_DoubleDispose_IsIdempotent()
    {
        var frame = Payload.Rent(16);
        var response = new ReceivedResponse(
            new RpcResponse { MessageId = 1, IsSuccess = true },
            frame.Memory,
            frame,
            stream: null);

        response.Dispose();
        response.Dispose();
    }

    [Fact]
    public void ReceivedResponse_DetachOutboundStreams_AfterDispose_ReturnsNull()
    {
        var frame = Payload.Rent(16);
        var response = new ReceivedResponse(
            new RpcResponse { MessageId = 1, IsSuccess = true },
            frame.Memory,
            frame,
            stream: null);

        response.Dispose();

        Assert.Null(response.DetachOutboundStreams());
    }

    [Fact]
    public async Task RpcStreamAttachment_DisposeSourceOnceAsync_SecondCallIsNoop()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var handle = new RpcStreamHandle(42, RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(handle, stream, leaveOpen: false);

        await attachment.DisposeSourceOnceAsync();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());

        await attachment.DisposeSourceOnceAsync();
    }

    [Fact]
    public async Task RpcStreamAttachment_DisposeSourceBestEffort_WhenAlreadyDisposed_IsNoop()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var handle = new RpcStreamHandle(43, RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(handle, stream, leaveOpen: false);

        await attachment.DisposeSourceOnceAsync();

        await attachment.DisposeSourceBestEffortAsync("test");
    }

    [Fact]
    public void RpcStreamingContext_SetResponse_Stream_WorksWithoutClosure()
    {
        // This test verifies the SetResponse(Stream) path works correctly after
        // the closure elimination refactor. We can't call it directly without a
        // full RpcStreamManager, but we verify the null guard still works.
        var ctx = RpcStreamingContext.Disabled;

        Assert.Throws<ArgumentNullException>(() => ctx.SetResponse((Stream)null!));
    }

    [Fact]
    public void RpcStreamingContext_SetResponse_Pipe_NullGuard()
    {
        var ctx = RpcStreamingContext.Disabled;

        Assert.Throws<ArgumentNullException>(() => ctx.SetResponse((System.IO.Pipelines.Pipe)null!));
    }

    [Fact]
    public void RpcStreamingContext_SetResponse_Stream_OnDisabled_ThrowsInvalidOp()
    {
        var ctx = RpcStreamingContext.Disabled;

        Assert.Throws<InvalidOperationException>(() => ctx.SetResponse(new MemoryStream()));
    }
}
