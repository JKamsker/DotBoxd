namespace SafeIR.Runtime;

using System.Net;
using System.Net.Sockets;

internal static class SafePinnedHttpTransport
{
    public static async ValueTask<HttpResponseMessage> SendAsync(
        SafeInMemoryHttpMessageInvoker? invoker,
        HttpRequestMessage message,
        IReadOnlyList<IPAddress> vettedAddresses,
        CancellationToken cancellationToken)
    {
        if (invoker is not null) {
            return await invoker.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        using var pinned = CreatePinnedInvoker(vettedAddresses);
        return await pinned.SendAsync(message, cancellationToken).ConfigureAwait(false);
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
