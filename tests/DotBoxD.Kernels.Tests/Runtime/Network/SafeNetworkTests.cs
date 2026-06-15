using System.Net;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Tests.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests;

public sealed class SafeNetworkTests
{
    [Fact]
    public async Task Http_get_is_denied_without_host_grant()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, NetworkPolicyBuilder().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Http_get_allows_configured_https_host_and_audits_sanitized_url()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("remote-config"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config?token=secret"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal("remote-config", ((StringValue)result.Value!).Value);
        Assert.Contains(result.AuditEvents, e =>
            e.BindingId == "net.http.get" &&
            e.ResourceId == "https://api.example.com/config" &&
            e.Success);
    }

    [Fact]
    public async Task Http_get_denies_hosts_outside_allowlist()
    {
        var result = await ExecuteNetworkAsync(
            "https://evil.example.com/config",
            NetworkPolicyBuilder()
                .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_non_default_port_without_authority_allowlist()
    {
        var result = await ExecuteNetworkAsync(
            "https://api.example.com:8443/config",
            NetworkPolicyBuilder()
                .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_allows_explicit_authority_and_audits_port()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com:8443/config?token=secret"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com:8443"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(result.AuditEvents, e =>
            e.BindingId == "net.http.get" &&
            e.ResourceId == "https://api.example.com:8443/config" &&
            e.Success);
    }

    [Fact]
    public async Task Http_get_denies_ip_literals_by_default()
    {
        var result = await ExecuteNetworkAsync(
            "https://127.0.0.1/config",
            NetworkPolicyBuilder()
                .GrantHttpGet(["127.0.0.1"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_private_ip_literals_unless_explicitly_allowed()
    {
        var result = await ExecuteNetworkAsync(
            "https://192.168.1.20/config",
            NetworkPolicyBuilder()
                .GrantHttpGet(["192.168.1.20"], 1024, allowIpLiterals: true)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    public async Task Http_get_denies_special_use_ip_literals_by_default(string address)
    {
        var result = await ExecuteNetworkAsync(
            $"https://{address}/config",
            NetworkPolicyBuilder()
                .GrantHttpGet([address], 1024, allowIpLiterals: true)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_allowed_hostname_that_resolves_to_private_network()
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("private"),
            dnsResolver: StaticDns(IPAddress.Loopback));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_allowed_hostname_with_empty_dns_result()
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("empty"),
            dnsResolver: StaticDns());
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_allows_private_dns_only_when_explicitly_granted()
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("private"),
            dnsResolver: StaticDns(IPAddress.Loopback));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], 1024, allowPrivateNetwork: true)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("private", ((StringValue)result.Value!).Value);
    }

    [Fact]
    public async Task Http_get_denies_redirect_responses()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker(
            "",
            HttpStatusCode.Redirect,
            "https://evil.example.com/config"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Bytes > 0);
    }

    [Fact]
    public async Task Http_get_charges_failed_response_metadata_before_status_failure()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("not-found", HttpStatusCode.NotFound));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.True(result.ResourceUsage.NetworkBytesRead > 0);
        Assert.Contains(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Bytes > 0);
    }

    [Fact]
    public async Task Http_get_enforces_failed_response_metadata_byte_limit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("not-found", HttpStatusCode.NotFound));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.True(result.ResourceUsage.NetworkBytesRead > 0);
        Assert.Contains(result.AuditEvents, e => e.BindingId == "net.http.get" && e.Bytes > 0);
    }

    [Fact]
    public async Task Http_get_denies_invoker_that_already_followed_redirect()
    {
        var host = SandboxTestHost.Create(networkInvoker: RedirectFollowedInvoker());
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Direct_policy_negative_http_timeout_is_rejected_at_prepare()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = new SandboxPolicy(
            "bad-http-timeout",
            SandboxEffects.Pure | SandboxEffect.Network,
            [
                new CapabilityGrant(
                    "net.http.get",
                    new Dictionary<string, string> {
                        ["allowedHosts"] = "api.example.com",
                        ["maxResponseBytes"] = "1024",
                        ["timeoutMs"] = "-1"
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxNetworkBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public async Task Http_get_enforces_response_byte_limit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("too-large"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 3)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_enforces_allocation_limit_while_streaming_body()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("too-large"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithMaxAllocatedBytes(4)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Deterministic_policy_denies_network_effects()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 1)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-DETERMINISM");
    }

}

