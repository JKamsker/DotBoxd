using System.Net;
using System.Net.Sockets;
using System.Text;
using SafeIR;
using static SafeIR.Tests.NetworkTestFixtures;

namespace SafeIR.Tests;

public sealed class PinnedHttpTransportTests
{
    [Fact]
    public async Task Default_http_transport_connects_to_vetted_dns_address()
    {
        using var server = new LoopbackHttpServer("pinned-response");
        var host = SandboxTestHost.Create(dnsResolver: StaticDns(IPAddress.Loopback));
        var uri = $"http://safe.test:{server.Port}/config";
        var module = await host.ParseJsonAsync(NetworkJson(uri));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(
                [$"safe.test:{server.Port}"],
                maxResponseBytes: 1024,
                allowedSchemes: ["http"],
                allowPrivateNetwork: true)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("pinned-response", ((StringValue)result.Value!).Value);
        await server.ResponseSent;
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;

        public LoopbackHttpServer(string response)
        {
            _response = response;
            _listener = new TcpListener(IPAddress.Loopback, port: 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            ResponseSent = RunAsync();
        }

        public int Port { get; }

        public Task ResponseSent { get; }

        public void Dispose() => _listener.Stop();

        private async Task RunAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            await using var stream = client.GetStream();
            await ReadHeadersAsync(stream).ConfigureAwait(false);
            var bytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/plain\r\n" +
                $"Content-Length: {_response.Length}\r\n" +
                "Connection: close\r\n\r\n" +
                _response);
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }

        private static async Task ReadHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            var window = new Queue<byte>(4);
            while (await stream.ReadAsync(buffer).ConfigureAwait(false) == 1) {
                if (window.Count == 4) {
                    window.Dequeue();
                }

                window.Enqueue(buffer[0]);
                if (window.SequenceEqual(new byte[] { 13, 10, 13, 10 })) {
                    return;
                }
            }
        }
    }
}
