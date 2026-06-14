using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Tests.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests;

public sealed class NetworkAuditTests
{
    [Fact]
    public async Task Http_get_audits_raw_response_bytes()
    {
        var rawBytes = new byte[] { 0xff };
        var host = SandboxTestHost.Create(networkInvoker: RawBytesInvoker(rawBytes));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(result.ResourceUsage.NetworkBytesRead > rawBytes.Length);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Success);
        Assert.Equal(result.ResourceUsage.NetworkBytesRead, audit.Bytes);
        Assert.Equal("network", audit.Fields!["resourceKind"]);
        Assert.Equal(
            result.ResourceUsage.NetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            audit.Fields["bytesRead"]);
        Assert.True(double.Parse(audit.Fields["durationMs"], System.Globalization.CultureInfo.InvariantCulture) >= 0);
    }

    private static SafeInMemoryHttpMessageInvoker RawBytesInvoker(byte[] rawBytes)
        => new(rawBytes);
}
