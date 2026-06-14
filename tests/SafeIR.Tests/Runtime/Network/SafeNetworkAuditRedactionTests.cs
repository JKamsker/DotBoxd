using SafeIR;
using static SafeIR.Tests.NetworkTestFixtures;

namespace SafeIR.Tests;

public sealed class SafeNetworkAuditRedactionTests
{
    [Fact]
    public async Task Http_get_redacts_secret_shaped_path_segments_in_audit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("remote-config"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/download/token/abc123?ignored=secret"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Success);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignored", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_get_redacts_secret_shaped_denied_path_segments_in_audit()
    {
        var result = await ExecuteNetworkAsync(
            "https://evil.example.com/download/token/abc123?ignored=secret",
            SandboxPolicyBuilder.Create()
                .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && !e.Success);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignored", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }
}
