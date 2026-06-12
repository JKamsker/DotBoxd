using SafeIR;
using static SafeIR.Tests.NetworkTestFixtures;

namespace SafeIR.Tests;

public sealed class SafeNetworkStreamingTests
{
    [Fact]
    public async Task Http_get_unknown_length_response_reads_only_one_byte_past_limit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("too-large"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 3)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(4, result.ResourceUsage.NetworkBytesRead);
    }
}
