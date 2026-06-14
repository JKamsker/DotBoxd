using DotBoxd.Kernels;
using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for CMP-0017: the public <see cref="SandboxHostBuilder.ForwardAuditEventsTo"/>
/// observer surface had no runnable, user-facing example. The implementation and unit tests existed
/// (<c>tests/DotBoxd.Kernels.Tests/Audit/AuditObserverTests.cs</c>), but nothing under <c>samples/Kernels/</c> showed
/// consumers how to wire an observer, prove observed events match
/// <see cref="SandboxExecutionResult.AuditEvents"/>, or prove a throwing observer is isolated. Because
/// none of the docs-smoke examples contained <c>ForwardAuditEventsTo</c>, a release could ship while the
/// documented observer contract drifted from the package surface.
///
/// The fix adds <c>samples/Kernels/Capabilities/DotBoxd.Kernels.Example.Capabilities/Examples/AuditObserverExample.cs</c>, wires
/// it into the Capabilities example runner (which the docs-smoke script executes), and links it from the
/// addendum examples doc. These tests pin both halves of the fix:
/// (1) the runnable example exists, is wired into the smoke-executed runner, and is documented; and
/// (2) the behavior the example demonstrates over the real public host API actually holds.
/// </summary>
public sealed class Fix_CMP_0017_Tests
{
    private const string ExampleRelative =
        "samples/Kernels/Capabilities/DotBoxd.Kernels.Example.Capabilities/Examples/AuditObserverExample.cs";

    private const string RunnerRelative =
        "samples/Kernels/Capabilities/DotBoxd.Kernels.Example.Capabilities/Program.cs";

    private const string DocsRelative = "docs/Specs/Addendum/Examples.md";

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
    public void Runnable_audit_observer_example_exists_and_wires_the_public_observer_api()
    {
        var example = ReadRepositoryText(ExampleRelative);

        Assert.True(
            example.Contains("ForwardAuditEventsTo", StringComparison.Ordinal),
            "Audit observer example must wire the public ForwardAuditEventsTo observer API.");
        // The example must demonstrate observer isolation (a throwing observer) and the result match.
        Assert.True(
            example.Contains("throw", StringComparison.Ordinal),
            "Audit observer example must demonstrate a throwing (isolated) observer.");
        Assert.True(
            example.Contains("SequenceEqual", StringComparison.Ordinal),
            "Audit observer example must prove observed events match result.AuditEvents.");
        Assert.True(
            example.Contains("AuditEvents", StringComparison.Ordinal),
            "Audit observer example must reference SandboxExecutionResult.AuditEvents.");
    }

    [Fact]
    public void Audit_observer_example_is_wired_into_the_smoke_executed_runner()
    {
        // The docs-smoke script runs the addendum example project, so the example only provides
        // release coverage if the runner actually invokes it.
        var runner = ReadRepositoryText(RunnerRelative);

        Assert.True(
            runner.Contains("AuditObserverExample.RunAsync", StringComparison.Ordinal),
            "The Capabilities example runner (Program.cs) must invoke AuditObserverExample so docs-smoke executes it.");
    }

    [Fact]
    public void Audit_observer_example_is_linked_from_the_public_examples_doc()
    {
        var docs = ReadRepositoryText(DocsRelative);

        Assert.True(
            docs.Contains("AuditObserverExample.cs", StringComparison.Ordinal),
            "Public examples doc must link the runnable audit observer example file.");
        Assert.True(
            docs.Contains("ForwardAuditEventsTo", StringComparison.Ordinal),
            "Public examples doc must document the ForwardAuditEventsTo observer surface.");
    }

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
            if (File.Exists(Path.Combine(current.FullName, "DotBoxd.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
