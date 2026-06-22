using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Tests.Coverage.Transport;

internal static class TcpTransportCoverageTestHelpers
{
    public static int RequirePort(TcpServerTransport server) =>
        server.LocalEndpoint?.Port ?? throw new InvalidOperationException("no bound port");

    public static int ReserveThenReleasePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public static Payload BuildFrame(int messageId, byte type, int bodyLength)
    {
        var total = MessageFramer.HeaderSize + bodyLength;
        var payload = Payload.Rent(total);
        var span = payload.Memory.Span;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), total);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = type;
        for (var i = MessageFramer.HeaderSize; i < total; i++)
        {
            span[i] = (byte)(i & 0xFF);
        }

        return payload;
    }
}
