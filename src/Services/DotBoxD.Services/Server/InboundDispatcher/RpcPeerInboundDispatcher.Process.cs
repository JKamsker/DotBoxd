using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer.Inbound;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;

namespace DotBoxD.Services.Server;

internal sealed partial class RpcPeerInboundDispatcher
{
    private void StartRequest(RpcPeerInboundRequest inbound)
    {
        if (!TryEnterActiveRequest())
        {
            inbound.Frame.Dispose();
            ReleaseRequest(inbound);
            return;
        }

        _ = ProcessTrackedRequestAsync(inbound);
    }

    private async Task ProcessTrackedRequestAsync(RpcPeerInboundRequest inbound)
    {
        try
        {
            await ProcessRequestAsync(inbound).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Tracked inbound request failed", ex);
        }
        finally
        {
            CompleteActiveRequest();
        }
    }

    private async Task ProcessRequestAsync(RpcPeerInboundRequest inbound)
    {
        var releaseRequest = true;
        RpcStreamingContext? streaming = null;
        try
        {
            using (inbound.Frame)
            {
                streaming = inbound.RequiresStreamingContext || inbound.Request.Streams is { Length: > 0 }
                    ? new RpcStreamingContext(
                        _streams,
                        _serializer,
                        inbound.CancellationToken,
                        inbound.Request.Streams)
                    : RpcStreamingContext.Disabled;
                using var response = await _responseBuilder.BuildAsync(
                    inbound.Request,
                    inbound.MessageId,
                    inbound.Body,
                    streaming,
                    inbound.Dispatcher,
                    inbound.CancellationToken).ConfigureAwait(false);
                var responseStream = response.Stream;
                try
                {
                    if (_sendFrameAsync is not null &&
                        response.TryDetachWriter(out var responseWriter))
                    {
                        await _sendFrameAsync(responseWriter, inbound.CancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _sendAsync(response.FrameMemory, inbound.CancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    if (responseStream is not null)
                    {
                        await streaming.AbandonResponseAsync().ConfigureAwait(false);
                    }

                    throw;
                }

                if (responseStream is not null)
                {
                    if (StartResponseStream(inbound, responseStream, streaming))
                    {
                        releaseRequest = false;
                    }
                    else
                    {
                        await streaming.AbandonResponseAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (inbound.IsCancellationRequested)
        {
            // Cancelled work sends no response frame.
        }
        catch (Exception ex)
        {
            _dispatchError(inbound, ex);
            RpcDiagnostics.Report("Inbound request dispatch failed", ex);
            try
            {
                using var errorFrame = _responseBuilder.BuildErrorFrame(
                    inbound.MessageId,
                    RpcErrors.FromException(ex, _exceptionTransformer));
                await _sendAsync(errorFrame.Memory, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort error response.
            }
        }
        finally
        {
            if (releaseRequest)
            {
                ReleaseRequest(inbound);
            }
        }
    }

    private bool StartResponseStream(
        RpcPeerInboundRequest inbound,
        RpcStreamAttachment stream,
        RpcStreamingContext streaming)
    {
        if (!TryEnterActiveStream())
        {
            return false;
        }

        _ = ProcessResponseStreamAsync(inbound, stream, streaming);
        return true;
    }

    private async Task ProcessResponseStreamAsync(
        RpcPeerInboundRequest inbound,
        RpcStreamAttachment stream,
        RpcStreamingContext streaming)
    {
        var registered = false;
        try
        {
            var outbound = _streams.RegisterOutbound(stream, inbound.CancellationToken);
            await using (outbound.ConfigureAwait(false))
            {
                registered = true;
                outbound.Start();
                await outbound.WaitAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!registered || inbound.IsCancellationRequested)
        {
            if (!registered)
            {
                await stream.DisposeSourceBestEffortAsync("Inbound response stream source cleanup failed")
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (!registered)
            {
                await stream.DisposeSourceBestEffortAsync("Inbound response stream source cleanup failed")
                    .ConfigureAwait(false);
            }

            _dispatchError(inbound, ex);
            RpcDiagnostics.Report("Inbound response stream failed", ex);
        }
        finally
        {
            CompleteActiveStream();
            ReleaseRequest(inbound);
        }
    }

    private void ReleaseRequest(RpcPeerInboundRequest inbound)
    {
        if (inbound.Request.Streams is { } streams)
        {
            foreach (var stream in streams)
            {
                _streams.RemoveInbound(stream.StreamId);
            }
        }

        _activeInbound.Remove(inbound.MessageId, inbound.RequestCts);
        inbound.RequestCts?.Dispose();
    }
}
