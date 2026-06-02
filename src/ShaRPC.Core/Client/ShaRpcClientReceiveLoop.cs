using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

internal sealed class ShaRpcClientReceiveLoop
{
    private readonly ITransport _transport;
    private readonly ISerializer _serializer;
    private readonly ShaRpcPendingRequests _pendingRequests;

    public ShaRpcClientReceiveLoop(
        ITransport transport,
        ISerializer serializer,
        ShaRpcPendingRequests pendingRequests)
    {
        _transport = transport;
        _serializer = serializer;
        _pendingRequests = pendingRequests;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _transport.IsConnected)
            {
                var connection = _transport.Connection;
                if (connection == null)
                {
                    break;
                }

                var frame = await connection.ReceiveAsync(ct).ConfigureAwait(false);
                if (frame.Length == 0)
                {
                    frame.Dispose();
                    break;
                }

                var handedOff = false;
                try
                {
                    if (!MessageFramer.TryReadFrame(frame.Memory, out var messageId, out var messageType, out var envelope, out var payload))
                    {
                        continue;
                    }

                    if (messageType != MessageType.Response && messageType != MessageType.Error)
                    {
                        continue;
                    }

                    RpcResponse response;
                    try
                    {
                        response = _serializer.Deserialize<RpcResponse>(envelope);
                    }
                    catch (Exception ex)
                    {
                        _pendingRequests.TryFail(
                            messageId,
                            new ShaRpcProtocolException("Malformed response envelope."));
                        RpcDiagnostics.Report("Response envelope deserialization failed", ex);
                        continue;
                    }

                    handedOff = _pendingRequests.TryComplete(messageId, response, payload, frame);
                }
                finally
                {
                    if (!handedOff)
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _pendingRequests.FailAll(new ShaRpcConnectionException("Connection lost.", ex));
        }
        finally
        {
            _pendingRequests.FailAll(new ShaRpcConnectionException("Connection closed."));
        }
    }
}
