namespace SafeIR.Example.Capabilities;

using SafeIR;
using SafeIR.Hosting;
using SafeIR.Serialization.Json;

/// <summary>
/// Standalone runtime proof for the safe logging capability. It deliberately uses the bare
/// <see cref="SandboxHost"/> surface (not the plugin server) so hosts can see the full boundary:
/// logging requires both <c>AddLogBindings()</c> at setup and <c>GrantLogging()</c> in the policy,
/// log messages are audited and sanitized, and <c>WithMaxLogEvents</c> / <c>WithMaxLogMessageLength</c>
/// are the quota controls that surface the documented <see cref="SandboxErrorCode.QuotaExceeded"/> shape.
/// </summary>
internal static class SafeLoggingExample
{
    private const string LoggingModuleJson = """
    {
      "id": "logging-walkthrough",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "log.write", "reason": "Emit operational logs" }],
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "Unit",
          "body": [
            { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "starting run token=abc123" }] } },
            { "op": "return", "value": { "call": "log.warn", "args": [{ "string": "low fuel" }] } }
          ]
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync(LoggingModuleJson);

        await RunGrantedAsync(host, module);
        await RunQuotaDeniedAsync(host, module);
    }

    private static async Task RunGrantedAsync(SandboxHost host, SandboxModule module)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithMaxLogEvents(8)
            .WithMaxLogMessageLength(256)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine($"logging granted: succeeded={result.Succeeded}, logEvents={result.ResourceUsage.LogEvents}");
        foreach (var audit in result.AuditEvents) {
            if (audit.Kind == "SandboxLog") {
                // Message is already sanitized/redacted; secret-shaped tokens never reach the sink.
                Console.WriteLine($"  audit {audit.ResourceId} cap={audit.CapabilityId}: {audit.Message}");
            }
        }
    }

    private static async Task RunQuotaDeniedAsync(SandboxHost host, SandboxModule module)
    {
        // One event is allowed, but the module emits two, so the second hits the documented quota.
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithMaxLogEvents(1)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine(
            $"logging quota: succeeded={result.Succeeded}, error={result.Error?.Code}");
    }
}
