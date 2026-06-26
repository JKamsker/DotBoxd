using System.Text;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class NetworkRequestQuotaTests
{
    [Fact]
    public async Task Http_get_enforces_request_byte_limit_and_tracks_written_bytes()
    {
        var allowed = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, maxRequestBytes: 128)
            .WithFuel(5_000)
            .Build();
        var allowedResult = await ExecuteNetworkAsync("https://api.example.com/config", allowed);
        Assert.True(allowedResult.Succeeded, allowedResult.Error?.SafeMessage);
        Assert.True(allowedResult.ResourceUsage.NetworkBytesWritten > 0);

        var denied = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, maxRequestBytes: 4)
            .WithFuel(5_000)
            .Build();

        var deniedResult = await ExecuteNetworkAsync("https://api.example.com/config", denied);

        Assert.False(deniedResult.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, deniedResult.Error!.Code);
    }

    [Fact]
    public async Task Http_get_request_byte_limit_matches_get_line_byte_count()
    {
        const string uri = "https://api.example.com/config?tenant=alpha&mode=full";
        var expectedBytes = 4 + Encoding.UTF8.GetByteCount(uri);
        var allowed = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, maxRequestBytes: expectedBytes)
            .WithFuel(5_000)
            .Build();

        var allowedResult = await ExecuteNetworkAsync(uri, allowed);

        Assert.True(allowedResult.Succeeded, allowedResult.Error?.SafeMessage);
        Assert.Equal(expectedBytes, allowedResult.ResourceUsage.NetworkBytesWritten);

        var denied = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, maxRequestBytes: expectedBytes - 1)
            .WithFuel(5_000)
            .Build();

        var deniedResult = await ExecuteNetworkAsync(uri, denied);

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
