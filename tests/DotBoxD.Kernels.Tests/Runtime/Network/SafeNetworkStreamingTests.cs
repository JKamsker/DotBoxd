using DotBoxD.Kernels;
using static DotBoxD.Kernels.Tests.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests;

public sealed class SafeNetworkStreamingTests
{
    [Fact]
    public async Task Http_get_unlimited_response_budget_does_not_overflow_read_limit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: long.MaxValue)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("ok", Assert.IsType<StringValue>(result.Value).Value);
        Assert.True(result.ResourceUsage.NetworkBytesRead > 2);
    }

    [Fact]
    public async Task Http_get_unknown_length_response_reads_only_one_byte_past_limit()
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
    }

    [Fact]
    public async Task Http_get_response_headers_count_toward_response_limit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker(""));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.True(result.ResourceUsage.NetworkBytesRead > 1);
    }
}
