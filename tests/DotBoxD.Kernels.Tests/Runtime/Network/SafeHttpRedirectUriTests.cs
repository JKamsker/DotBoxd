using System.Reflection;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpRedirectUriTests
{
    [Fact]
    public void MatchesAllowedAuthority_set_preserves_case_insensitive_string_match()
    {
        var allowedHosts = new HashSet<string>(StringComparer.Ordinal) { "API.EXAMPLE.COM" };

        Assert.True(MatchesAllowedAuthority(allowedHosts, new Uri("https://api.example.com/config")));
    }

    [Fact]
    public async Task Http_get_allows_equal_explicit_port_final_request_uri()
    {
        var host = SandboxTestHost.Create(networkInvoker: new(
            "ok",
            finalRequestUri: "https://api.example.com:8443/config"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com:8443/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com:8443"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
    }

    private static bool MatchesAllowedAuthority(IReadOnlySet<string> allowedHosts, Uri uri)
    {
        var type = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.SafeHttpUriAudit",
            throwOnError: true)!;
        var method = type.GetMethod(
            "MatchesAllowedAuthority",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(IReadOnlySet<string>), typeof(Uri)],
            modifiers: null)!;

        return (bool)method.Invoke(null, [allowedHosts, uri])!;
    }
}
