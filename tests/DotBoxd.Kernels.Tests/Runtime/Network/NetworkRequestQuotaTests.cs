using DotBoxd.Kernels;
using static DotBoxd.Kernels.Tests.NetworkTestFixtures;

namespace DotBoxd.Kernels.Tests;

public sealed class NetworkRequestQuotaTests
{
    [Fact]
    public async Task Http_get_enforces_request_byte_limit_and_tracks_written_bytes()
    {
        var allowed = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, maxRequestBytes: 128)
            .WithFuel(5_000)
            .Build();
        var allowedResult = await ExecuteNetworkAsync("https://api.example.com/config", allowed);
        Assert.True(allowedResult.Succeeded, allowedResult.Error?.SafeMessage);
        Assert.True(allowedResult.ResourceUsage.NetworkBytesWritten > 0);

        var denied = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, maxRequestBytes: 4)
            .WithFuel(5_000)
            .Build();

        var deniedResult = await ExecuteNetworkAsync("https://api.example.com/config", denied);

        Assert.False(deniedResult.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, deniedResult.Error!.Code);
    }

    private static async Task<SandboxExecutionResult> ExecuteNetworkAsync(string uri, SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson(uri));
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }
}
