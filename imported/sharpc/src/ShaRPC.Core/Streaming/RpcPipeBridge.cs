using System.IO.Pipelines;

namespace ShaRPC.Core.Streaming;

internal static class RpcPipeBridge
{
    public static Pipe CreateReadablePipe(RpcStreamReceiver receiver, CancellationToken ct)
    {
        var pipe = new Pipe();
#pragma warning disable CS0618
        pipe.Writer.OnReaderCompleted(static (_, state) => ((RpcStreamReceiver)state!).Cancel(), receiver);
#pragma warning restore CS0618
        _ = PumpAsync(receiver, pipe.Writer, ct);
        return pipe;
    }

    private static async Task PumpAsync(
        RpcStreamReceiver receiver,
        PipeWriter writer,
        CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var chunk = await receiver.ReadChunkAsync(ct).ConfigureAwait(false);
                if (chunk is null)
                {
                    await writer.CompleteAsync().ConfigureAwait(false);
                    return;
                }

                using (chunk)
                {
                    var memory = writer.GetMemory(chunk.Payload.Length);
                    chunk.Payload.CopyTo(memory);
                    writer.Advance(chunk.Payload.Length);
                    var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (flush.IsCanceled || flush.IsCompleted)
                    {
                        receiver.Cancel();
                        await writer.CompleteAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            receiver.Cancel();
            await writer.CompleteAsync(ex).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex).ConfigureAwait(false);
        }
    }
}
