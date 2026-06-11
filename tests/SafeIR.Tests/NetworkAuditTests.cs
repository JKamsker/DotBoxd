using SafeIR.Runtime;
using static SafeIR.Tests.NetworkTestFixtures;

namespace SafeIR.Tests;

public sealed class NetworkAuditTests
{
    [Fact]
    public async Task Http_get_audits_raw_response_bytes()
    {
        var rawBytes = new byte[] { 0xff };
        var host = SandboxTestHost.Create(networkInvoker: RawBytesInvoker(rawBytes));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(rawBytes.Length, result.ResourceUsage.NetworkBytesRead);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Success);
        Assert.Equal(rawBytes.Length, audit.Bytes);
    }

    private static SafeInMemoryHttpMessageInvoker RawBytesInvoker(byte[] rawBytes)
        => new(rawBytes);
}
