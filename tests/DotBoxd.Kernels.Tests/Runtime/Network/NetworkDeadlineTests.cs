using System.Diagnostics;
using System.Net;
using DotBoxd.Kernels.Runtime;
using static DotBoxd.Kernels.Tests.NetworkTestFixtures;

namespace DotBoxd.Kernels.Tests;

public sealed class NetworkDeadlineTests
{
    [Fact]
    public async Task Http_get_caps_dns_timeout_to_remaining_wall_time()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("unused"), dnsResolver: SlowDns());
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = LongRequestShortWallPolicy();
        var plan = await host.PrepareAsync(module, policy);
        var elapsed = Stopwatch.StartNew();

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        elapsed.Stop();
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"elapsed {elapsed.Elapsed}");
    }

    [Fact]
    public async Task Http_get_caps_send_timeout_to_remaining_wall_time()
    {
        var host = SandboxTestHost.Create(networkInvoker: SlowInvoker(), dnsResolver: StaticDns(IPAddress.Parse("93.184.216.34")));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = LongRequestShortWallPolicy();
        var plan = await host.PrepareAsync(module, policy);
        var elapsed = Stopwatch.StartNew();

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        elapsed.Stop();
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"elapsed {elapsed.Elapsed}");
    }

    private static SandboxPolicy LongRequestShortWallPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024, timeout: TimeSpan.FromSeconds(5))
            .WithWallTime(TimeSpan.FromMilliseconds(50))
            .WithFuel(5_000)
            .Build();

    private static SafeDnsResolver SlowDns()
        => async (_, cancellationToken) => {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return [IPAddress.Parse("93.184.216.34")];
        };

    private static SafeInMemoryHttpMessageInvoker SlowInvoker()
        => new("late", responseDelay: TimeSpan.FromSeconds(5));
}
