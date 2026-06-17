using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

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

    [Fact]
    public async Task Http_get_quota_failure_audits_streamed_response_bytes()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("too-large"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 22)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(23, result.ResourceUsage.NetworkBytesRead);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && !e.Success);
        Assert.Equal(result.ResourceUsage.NetworkBytesRead, audit.Bytes);
        Assert.Equal(
            result.ResourceUsage.NetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            audit.Fields!["bytesRead"]);
    }

    private static SafeInMemoryHttpMessageInvoker RawBytesInvoker(byte[] rawBytes)
        => new(rawBytes);
}
