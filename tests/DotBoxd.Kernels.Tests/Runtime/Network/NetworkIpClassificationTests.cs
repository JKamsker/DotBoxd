using System.Net;
using static DotBoxd.Kernels.Tests.NetworkTestFixtures;

namespace DotBoxd.Kernels.Tests;

public sealed class NetworkIpClassificationTests
{
    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("2001::1")]
    [InlineData("2001:2::1")]
    [InlineData("2001:10::1")]
    [InlineData("2001:20::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002::1")]
    [InlineData("3fff::1")]
    public async Task Http_get_denies_hostname_resolving_to_special_use_ipv6(string address)
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("blocked"),
            dnsResolver: StaticDns(IPAddress.Parse(address)));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_hostname_resolving_to_special_use_ipv4()
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("blocked"),
            dnsResolver: StaticDns(IPAddress.Parse("192.88.99.2")));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }
}
