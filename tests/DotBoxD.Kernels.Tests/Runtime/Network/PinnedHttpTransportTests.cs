using System.Net;
using System.Net.Sockets;
using System.Text;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class PinnedHttpTransportTests
{
    [Fact]
    public async Task Default_http_transport_ignores_ambient_proxy()
    {
        using var target = new LoopbackHttpServer("target-response");
        using var proxy = new LoopbackHttpServer("proxy-response");
        var previousProxy = HttpClient.DefaultProxy;
        try
        {
            HttpClient.DefaultProxy = new WebProxy($"http://127.0.0.1:{proxy.Port}");
            var result = await ExecutePinnedAsync(target.Port);

            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal("target-response", ((StringValue)result.Value!).Value);
            await target.ResponseSent;
            Assert.False(await proxy.WaitForResponseAsync(TimeSpan.FromMilliseconds(50)));
        }
        finally
        {
            HttpClient.DefaultProxy = previousProxy;
        }
    }

    [Fact]
    public async Task Default_http_transport_connects_to_vetted_dns_address()
    {
        using var server = new LoopbackHttpServer("pinned-response");
        var result = await ExecutePinnedAsync(server.Port);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("pinned-response", ((StringValue)result.Value!).Value);
        await server.ResponseSent;
    }

    [Fact]
    public async Task Default_http_transport_keeps_pinned_handler_alive_until_body_is_read()
    {
        using var server = new LoopbackHttpServer("delayed-response", bodyDelay: TimeSpan.FromMilliseconds(100));
        var result = await ExecutePinnedAsync(server.Port);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("delayed-response", ((StringValue)result.Value!).Value);
        await server.ResponseSent;
    }

    private static async Task<SandboxExecutionResult> ExecutePinnedAsync(int port)
    {
        var host = SandboxTestHost.Create(dnsResolver: StaticDns(IPAddress.Loopback));
        var uri = $"http://safe.test:{port}/config";
        var module = await host.ImportJsonAsync(NetworkJson(uri));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(
                [$"safe.test:{port}"],
                maxResponseBytes: 1024,
                allowedSchemes: ["http"],
                allowPrivateNetwork: true,
                timeout: TimeSpan.FromSeconds(10))
            .WithWallTime(TimeSpan.FromSeconds(10))
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _response;
        private readonly TimeSpan _bodyDelay;
        private bool _disposed;

        public LoopbackHttpServer(string response, TimeSpan? bodyDelay = null)
        {
            _response = response;
            _bodyDelay = bodyDelay ?? TimeSpan.Zero;
            _listener = new TcpListener(IPAddress.Loopback, port: 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            ResponseSent = RunAsync();
        }

        public int Port { get; }

        public Task ResponseSent { get; }

        public void Dispose()
        {
            _disposed = true;
            _listener.Stop();
        }

        public async Task<bool> WaitForResponseAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(ResponseSent, Task.Delay(timeout)).ConfigureAwait(false);
            return completed == ResponseSent && ResponseSent.IsCompletedSuccessfully;
        }

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                await using var stream = client.GetStream();
                await ReadHeadersAsync(stream).ConfigureAwait(false);
                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/plain\r\n" +
                    $"Content-Length: {_response.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(headers).ConfigureAwait(false);
                if (_bodyDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_bodyDelay).ConfigureAwait(false);
                }

                var body = Encoding.ASCII.GetBytes(_response);
                await stream.WriteAsync(body).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (_disposed)
            {
            }
            catch (SocketException) when (_disposed)
            {
            }
        }

        private static async Task ReadHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            var window = new Queue<byte>(4);
            while (await stream.ReadAsync(buffer).ConfigureAwait(false) == 1)
            {
                if (window.Count == 4)
                {
                    window.Dequeue();
                }

                window.Enqueue(buffer[0]);
                if (window.SequenceEqual(new byte[] { 13, 10, 13, 10 }))
                {
                    return;
                }
            }
        }
    }
}
