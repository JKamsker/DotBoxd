using ShaRPC.Core.Client;
using ShaRPC.Core.Streaming;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    private static async ValueTask DisposeStreamSourcesBestEffortAsync(RpcStreamAttachment[]? streams)
    {
        if (streams is null)
        {
            return;
        }

        foreach (var stream in streams)
        {
            if (stream is null)
            {
                continue;
            }

            await stream.DisposeSourceBestEffortAsync("Outbound stream source cleanup failed")
                .ConfigureAwait(false);
        }
    }

    private static async Task<ReceivedResponse> DisposeStreamSourcesAndThrowAsync(
        RpcStreamAttachment[]? streams,
        Exception error)
    {
        try
        {
            await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Outbound stream source cleanup failed", ex);
        }

        ThrowOriginal(error);
        return null!;
    }

    private static async Task<ReceivedResponse> CleanupOutboundSetupFailureAsync(
        RpcOutboundStreamSet outboundStreams,
        RpcStreamAttachment[]? streams,
        bool registeredStreams,
        Exception error)
    {
        try
        {
            await outboundStreams.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Outbound stream cleanup failed", ex);
        }

        if (!registeredStreams)
        {
            try
            {
                await DisposeStreamSourcesBestEffortAsync(streams).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report("Outbound stream source cleanup failed", ex);
            }
        }

        ThrowOriginal(error);
        return null!;
    }

    [DoesNotReturn]
    private static void ThrowOriginal(Exception error) =>
        ExceptionDispatchInfo.Capture(error).Throw();
}
