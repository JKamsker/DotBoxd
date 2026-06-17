using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

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
            .WithWallTime(TimeSpan.FromSeconds(2))
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
    public async Task Http_get_redacts_secret_key_value_path_segments_in_audit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("remote-config"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/download/api_key=abc123/file"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Success);
        Assert.DoesNotContain("api_key", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://api.example.com/download/token%2Fabc123/file", "token")]
    [InlineData("https://api.example.com/download/api-key%3Dabc123/file", "api-key")]
    public async Task Http_get_redacts_percent_encoded_secret_path_segments_in_audit(
        string uri,
        string marker)
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("remote-config"));
        var module = await host.ImportJsonAsync(NetworkJson(uri));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Success);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(marker, audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_get_redacts_secret_shaped_denied_path_segments_in_audit()
    {
        var result = await ExecuteNetworkAsync(
            "https://evil.example.com/download/token/abc123?ignored=secret",
            SandboxPolicyBuilder.Create()
                .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .WithWallTime(TimeSpan.FromSeconds(2))
                .Build());

        Assert.False(result.Succeeded);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "net.http.get" && !e.Success);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignored", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }
}
