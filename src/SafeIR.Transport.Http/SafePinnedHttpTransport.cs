namespace SafeIR.Runtime;

using System.Net;
using System.Net.Sockets;

internal static class SafePinnedHttpTransport
{
    public static async ValueTask<SafePinnedHttpResponse> SendAsync(
        SafeInMemoryHttpMessageInvoker? invoker,
        HttpRequestMessage message,
        IReadOnlyList<IPAddress> vettedAddresses,
        CancellationToken cancellationToken)
    {
        if (invoker is not null) {
            var response = await invoker.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return new SafePinnedHttpResponse(response, null);
        }

        var pinned = CreatePinnedInvoker(vettedAddresses);
        try {
            var response = await pinned.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return new SafePinnedHttpResponse(response, pinned);
        }
        catch {
            pinned.Dispose();
            throw;
        }
    }

    private static HttpMessageInvoker CreatePinnedInvoker(IReadOnlyList<IPAddress> vettedAddresses)
    {
        var handler = new SocketsHttpHandler {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) => {
                foreach (var address in vettedAddresses) {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try {
                        await socket.ConnectAsync(
                                new IPEndPoint(address, context.DnsEndPoint.Port),
                                cancellationToken)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception) when (cancellationToken.IsCancellationRequested) {
                        socket.Dispose();
                        throw;
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) {
                        socket.Dispose();
                    }
                }

                throw new SocketException((int)SocketError.HostUnreachable);
            }
        };
        return new HttpMessageInvoker(handler, disposeHandler: true);
    }
}

internal sealed class SafePinnedHttpResponse(HttpResponseMessage message, IDisposable? owner) : IDisposable
{
    public HttpResponseMessage Message { get; } = message;

    public void Dispose()
    {
        Message.Dispose();
        owner?.Dispose();
    }
}
