using System.Net;
using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;
using SafeIR.Serialization.Json;
using SafeIR.Transport.Http;

namespace SafeIR.Tests;

// Regression guard for CMP-0013: the maintained HTTP transport example must keep proving the
// consumer-facing safe setup (AddNetworkBindings + GrantHttpGet) through the public package
// surface, with one allowed request and one denied out-of-allowlist request. This mirrors what
// examples/HttpTransport runs, so behavior drift in the documented setup is caught here too.
public sealed class Fix_CMP_0013_Tests
{
    private const string AllowedHost = "api.example.com";

    [Fact]
    public async Task Documented_http_setup_allows_granted_host_and_audits_sanitized_url()
    {
        using var host = CreateExampleHost();
        var policy = ExamplePolicy();
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com/config?token=secret"));
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("remote-config", Assert.IsType<StringValue>(result.Value).Value);
        Assert.Contains(result.AuditEvents, e =>
            e.BindingId == "net.http.get" &&
            e.Success &&
            e.ResourceId == "https://api.example.com/config");
    }

    [Fact]
    public async Task Documented_http_setup_denies_host_outside_allowlist()
    {
        using var host = CreateExampleHost();
        var policy = ExamplePolicy();
        var module = await host.ImportJsonAsync(NetworkJson("https://evil.example.com/config"));
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    private static SandboxHost CreateExampleHost()
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddNetworkBindings(
                new SafeInMemoryHttpMessageInvoker("remote-config"),
                static (_, _) => ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]));
            builder.UseInterpreter();
        });

    private static SandboxPolicy ExamplePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantHttpGet([AllowedHost], maxResponseBytes: 1024, timeout: TimeSpan.FromSeconds(1))
            .WithFuel(5_000)
            .Build();

    private static string NetworkJson(string uri)
        => $$"""
        {
          "id": "http-transport-example",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "net.http.get" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "net.http.get",
                    "args": [{ "uri": "{{uri}}" }]
                  }
                }
              ]
            }
          ]
        }
        """;
}
