using System.Net;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

internal static class NetworkTestFixtures
{
    public static async ValueTask<SandboxExecutionResult> ExecuteNetworkAsync(string uri, SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ImportJsonAsync(NetworkJson(uri));
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    public static string NetworkJson(string uri)
        => $$"""
        {
          "id": "network-reader",
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

    public static SafeInMemoryHttpMessageInvoker FakeInvoker(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? location = null)
        => new(response, statusCode, location);

    public static SafeInMemoryHttpMessageInvoker RedirectFollowedInvoker()
        => new("redirected", finalRequestUri: "https://evil.example.com/config");

    public static SafeDnsResolver StaticDns(params IPAddress[] addresses)
        => (_, _) => ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
}
