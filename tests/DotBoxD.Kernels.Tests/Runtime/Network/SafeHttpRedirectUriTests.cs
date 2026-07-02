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

    [Theory]
    [InlineData("api.example.com:443", "https://api.example.com/config")]
    [InlineData("api.example.com:443", "https://api.example.com:443/config")]
    [InlineData("api.example.com:80", "http://api.example.com/config")]
    [InlineData("[::1]:443", "https://[::1]/config")]
    public void MatchesAllowedAuthority_allows_explicit_default_port_for_uri_scheme(
        string allowedHost,
        string uri)
    {
        Assert.True(MatchesAllowedAuthority(AllowedHosts(allowedHost), new Uri(uri)));
    }

    [Theory]
    [InlineData("api.example.com:80", "https://api.example.com/config")]
    [InlineData("api.example.com:443", "http://api.example.com/config")]
    public void MatchesAllowedAuthority_rejects_default_port_for_other_scheme(
        string allowedHost,
        string uri)
    {
        Assert.False(MatchesAllowedAuthority(AllowedHosts(allowedHost), new Uri(uri)));
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

    private static HashSet<string> AllowedHosts(string allowedHost)
        => new(StringComparer.Ordinal) { allowedHost };
}
