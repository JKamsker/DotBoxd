using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeNetworkGrantValidationTests
{
    [Theory]
    [InlineData("api.example.com,evil.example", "https")]
    [InlineData("api.example.com", "https,http")]
    public void Http_grant_builder_rejects_comma_delimited_allowlist_entries(
        string allowedHost,
        string allowedScheme)
    {
        Assert.Throws<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHttpGet(
                [allowedHost],
                maxResponseBytes: 1024,
                allowedSchemes: [allowedScheme]));
    }

    [Theory]
    [MemberData(nameof(InvalidTimeouts))]
    public void Http_grant_builder_rejects_timeouts_outside_serialized_range(TimeSpan timeout)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SandboxPolicyBuilder.Create().GrantHttpGet(
                ["api.example.com"],
                maxResponseBytes: 1024,
                timeout: timeout));
    }

    [Theory]
    [InlineData("api.example.com,,evil.example", "https")]
    [InlineData("https://api.example.com", "https")]
    [InlineData("api.example.com", "ftp")]
    public async Task Direct_http_grant_rejects_malformed_allowlist_tokens(
        string allowedHosts,
        string allowedSchemes)
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = new SandboxPolicy(
            "bad-http-allowlist",
            SandboxEffects.Pure | SandboxEffect.Network,
            [
                new CapabilityGrant(
                    "net.http.get",
                    new Dictionary<string, string> {
                        ["allowedHosts"] = allowedHosts,
                        ["allowedSchemes"] = allowedSchemes,
                        ["maxResponseBytes"] = "1024"
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxNetworkBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public async Task Wildcard_http_grant_rejects_malformed_allowlist_tokens()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = new SandboxPolicy(
            "bad-wildcard-http-allowlist",
            SandboxEffects.Pure | SandboxEffect.Network | SandboxEffect.Concurrency,
            [
                new CapabilityGrant(RuntimeCapabilityIds.Async, new Dictionary<string, string>()),
                new CapabilityGrant(
                    "net.http.*",
                    new Dictionary<string, string> {
                        ["allowedHosts"] = "api.example.com,,evil.example",
                        ["allowedSchemes"] = "https",
                        ["maxResponseBytes"] = "1024"
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxNetworkBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    public static TheoryData<TimeSpan> InvalidTimeouts()
        => new()
        {
            TimeSpan.Zero,
            TimeSpan.FromTicks(1),
            TimeSpan.FromMilliseconds(60_001)
        };
}
