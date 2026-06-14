using System.Net;
using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;
using SafeIR.Serialization.Json;
using SafeIR.Transport.Http;

// Resolves allowlisted hosts to a fixed public address so the private-network gate stays
// deterministic. Production transport performs the real vetted DNS lookup instead.
SafeDnsResolver exampleDns =
    (_, _) => ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);

// Safe HTTP transport setup. The in-memory invoker plus the fixed DNS resolver keep this example
// deterministic; production hosts let SafeIR.Transport.Http own the real pinned network path,
// including the live DNS lookup that the resolver stands in for here.
using var host = SandboxHost.Create(builder => {
    builder.AddDefaultPureBindings();
    builder.AddNetworkBindings(
        new SafeInMemoryHttpMessageInvoker("remote-config"),
        exampleDns);
    builder.UseInterpreter();
});

// The policy only grants what the module is allowed to reach: a single HTTPS host,
// with explicit response-byte and wall-time limits. Everything else stays denied.
var policy = SandboxPolicyBuilder.Create()
    .GrantHttpGet(
        ["api.example.com"],
        maxResponseBytes: 1024,
        timeout: TimeSpan.FromSeconds(1))
    .WithFuel(5_000)
    .Build();

var allowed = await RunAsync(host, policy, "https://api.example.com/config?token=secret");
RequireSuccess(allowed, "allowed host returns the remote body");

var denied = await RunAsync(host, policy, "https://evil.example.com/config");
RequireDenied(denied, "out-of-allowlist host is denied at execution");

Console.WriteLine("HTTP transport example smoke test passed.");

static async Task<SandboxExecutionResult> RunAsync(SandboxHost host, SandboxPolicy policy, string uri)
{
    var module = await host.ImportJsonAsync(NetworkJson(uri));
    var plan = await host.PrepareAsync(module, policy);
    return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
}

static void RequireSuccess(SandboxExecutionResult result, string description)
{
    if (!result.Succeeded || result.Value is not StringValue body)
    {
        throw new InvalidOperationException(
            $"Expected {description}, but got {result.Error?.SafeMessage ?? "no value"}.");
    }

    var audit = result.AuditEvents.FirstOrDefault(e => e.BindingId == "net.http.get" && e.Success);
    if (audit is null)
    {
        throw new InvalidOperationException($"Expected {description} to emit a successful audit event.");
    }

    // The audited resource is sanitized: the query string and its secret token are dropped.
    Console.WriteLine($"Allowed  -> body='{body.Value}' audit='{audit.ResourceId}'");
}

static void RequireDenied(SandboxExecutionResult result, string description)
{
    if (result.Succeeded || result.Error?.Code != SandboxErrorCode.PermissionDenied)
    {
        throw new InvalidOperationException(
            $"Expected {description}, but the request was not denied as expected.");
    }

    Console.WriteLine($"Denied   -> {result.Error.Code}: {result.Error.SafeMessage}");
}

static string NetworkJson(string uri)
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
