using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Regression;

/// <summary>
/// Regression coverage for CMP-0017: the public <see cref="SandboxHostBuilder.ForwardAuditEventsTo"/>
/// observer surface had no runnable, user-facing example. The implementation and unit tests existed
/// (<c>tests/DotBoxD.Kernels.Tests/Audit/AuditObserverTests.cs</c>), but nothing under <c>samples/Kernels/</c> showed
/// consumers how to wire an observer, prove observed events match
/// <see cref="SandboxExecutionResult.AuditEvents"/>, or prove a throwing observer is isolated. Because
/// none of the docs-smoke examples contained <c>ForwardAuditEventsTo</c>, a release could ship while the
/// documented observer contract drifted from the package surface.
///
/// The old runnable audit-observer example is no longer maintained. These tests keep the behavior
/// covered over the real public host API and require the example gap to be documented.
/// </summary>
public sealed class Fix_CMP_0017_Tests
{
    private const string ScoringModuleJson = """
    {
      "id": "audit-observer-regression",
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

    [Fact]
    public async Task Throwing_observer_is_isolated_and_surviving_observer_sees_sequenced_result_events()
    {
        // Exercises the exact public wiring the example demonstrates: a failing observer must not
        // change the result, and a later observer must still receive every sequenced audit event.
        var observed = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(_ => throw new InvalidOperationException("telemetry sink offline"));
            builder.ForwardAuditEventsTo(observed.Add);
        });

        var module = await host.ImportJsonAsync(ScoringModuleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(3), SandboxValue.FromInt32(25)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.NotEmpty(result.AuditEvents);
        Assert.Equal(result.AuditEvents, observed);
        Assert.True(
            IsInSequenceOrder(observed),
            "Forwarded audit events must arrive in non-decreasing SequenceNumber order.");
        Assert.Contains(observed, e => e.Kind == "RunSummary");
    }

    [Fact]
    public void Removed_audit_observer_sample_is_listed_as_an_example_coverage_gap()
    {
        var gaps = ReadRepositoryText("docs-site/src/content/docs/examples/coverage-gaps.md");

        Assert.Contains("Standalone audit-observer demonstrations", gaps);
    }

    private static bool IsInSequenceOrder(IReadOnlyList<SandboxAuditEvent> events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            if (events[index].SequenceNumber < events[index - 1].SequenceNumber)
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
