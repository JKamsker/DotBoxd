namespace DotBoxd.Kernels.Example.Capabilities;

using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Runnable proof for the public <see cref="SandboxHostBuilder.ForwardAuditEventsTo"/> observer
/// surface. Operational hosts wire audit observers to stream telemetry to billing, incident review,
/// or compliance export. This example shows the documented contract end to end:
/// observed events match <see cref="SandboxExecutionResult.AuditEvents"/> in sequence order, and a
/// throwing observer is isolated so it neither changes the returned result nor blocks a later
/// observer from receiving the same sequenced events.
/// </summary>
internal static class AuditObserverExample
{
    private const string ScoringModuleJson = """
    {
      "id": "audit-observer-walkthrough",
      "version": "1.0.0",
      "targetSandboxVersion": "1.0.0",
      "capabilityRequests": [],
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [
            { "name": "level", "type": "I32" },
            { "name": "rarity", "type": "I32" }
          ],
          "returnType": "I32",
          "body": [
            {
              "op": "set",
              "name": "base",
              "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
            },
            {
              "op": "return",
              "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "rarity" } }
            }
          ]
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        var observed = new List<SandboxAuditEvent>();

        // The first observer fails on every event. Forwarding failures are operational, so they must
        // stay isolated: the result is unaffected and the recording observer still sees every event.
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(_ => throw new InvalidOperationException("telemetry sink offline"));
            builder.ForwardAuditEventsTo(observed.Add);
        });

        var module = await host.ImportJsonAsync(ScoringModuleJson);
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(3), SandboxValue.FromInt32(25)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // The throwing observer did not change the returned result, and the surviving observer
        // received exactly the result's audit events, in sequence order.
        var matchesResult = observed.SequenceEqual(result.AuditEvents);
        var inSequenceOrder = IsInSequenceOrder(observed);
        Console.WriteLine(
            $"audit observer: succeeded={result.Succeeded}, auditEvents={result.AuditEvents.Count}, " +
            $"observedMatchesResult={matchesResult}, sequenceOrdered={inSequenceOrder}");

        foreach (var auditEvent in observed) {
            Console.WriteLine($"  #{auditEvent.SequenceNumber} {auditEvent.Kind} success={auditEvent.Success}");
        }
    }

    private static bool IsInSequenceOrder(IReadOnlyList<SandboxAuditEvent> events)
    {
        for (var index = 1; index < events.Count; index++) {
            if (events[index].SequenceNumber < events[index - 1].SequenceNumber) {
                return false;
            }
        }

        return true;
    }
}
