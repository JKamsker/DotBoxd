using SafeIR;
using SafeIR.Hosting;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for CMP-0019: the public non-fuel resource-limit surface
/// (<c>WithMaxLoopIterations</c>, <c>WithMaxHostCalls</c>, <c>WithWallTime</c>, <c>WithMaxListLength</c>,
/// <c>WithMaxStringLength</c>, and the other <see cref="SandboxPolicyBuilder"/> quota knobs) was proven
/// only by internal unit tests. The visible consumer-facing examples demonstrated <c>WithFuel</c> and
/// one host-call default, so integrators had no runnable pattern for configuring the other limits or for
/// recognizing the documented <see cref="SandboxErrorCode.QuotaExceeded"/> / <see cref="SandboxErrorCode.Timeout"/>
/// results and the matching <see cref="SandboxResourceUsage"/> counters. A release could regress non-fuel
/// wiring while docs-smoke still passed because no user-facing sample exercised those knobs.
///
/// The fix adds <c>examples/Capabilities/SafeIR.Example.Capabilities/Examples/ResourceLimitsExample.cs</c>, wires
/// it into the Capabilities example runner (which the docs-smoke script executes), and links it from the
/// addendum examples doc. These tests pin both halves of the fix: (1) the runnable example exists, is
/// wired into the smoke-executed runner, and is documented; and (2) the behavior the example demonstrates
/// over the real public host API actually holds.
/// </summary>
public sealed class Fix_CMP_0019_Tests
{
    private const string ExampleRelative =
        "examples/Capabilities/SafeIR.Example.Capabilities/Examples/ResourceLimitsExample.cs";

    private const string RunnerRelative =
        "examples/Capabilities/SafeIR.Example.Capabilities/Program.cs";

    private const string DocsRelative = "docs/Specs/Addendum/Examples.md";

    [Fact]
    public void Runnable_resource_limits_example_exists_and_wires_the_public_non_fuel_knobs()
    {
        var example = ReadRepositoryText(ExampleRelative);

        foreach (var knob in new[]
        {
            "WithMaxLoopIterations",
            "WithMaxHostCalls",
            "WithWallTime",
            "WithMaxListLength",
            "WithMaxStringLength"
        })
        {
            Assert.True(
                example.Contains(knob, StringComparison.Ordinal),
                $"Resource-limits example must demonstrate the public {knob} quota knob.");
        }

        // The proof must surface the public result codes and usage counters, not just configure limits.
        Assert.True(
            example.Contains("ResourceUsage.LoopIterations", StringComparison.Ordinal),
            "Resource-limits example must read back the LoopIterations usage counter.");
        Assert.True(
            example.Contains("ResourceUsage.HostCalls", StringComparison.Ordinal),
            "Resource-limits example must read back the HostCalls usage counter.");
        Assert.True(
            example.Contains("Error?.Code", StringComparison.Ordinal),
            "Resource-limits example must surface the public SandboxErrorCode result.");
    }

    [Fact]
    public void Resource_limits_example_is_wired_into_the_smoke_executed_runner()
    {
        // The docs-smoke script runs the addendum example project, so the example only provides
        // release coverage if the runner actually invokes it.
        var runner = ReadRepositoryText(RunnerRelative);

        Assert.True(
            runner.Contains("ResourceLimitsExample.RunAsync", StringComparison.Ordinal),
            "The Capabilities example runner (Program.cs) must invoke ResourceLimitsExample so docs-smoke executes it.");
    }

    [Fact]
    public void Resource_limits_example_is_linked_from_the_public_examples_doc()
    {
        var docs = ReadRepositoryText(DocsRelative);

        Assert.True(
            docs.Contains("ResourceLimitsExample.cs", StringComparison.Ordinal),
            "Public examples doc must link the runnable resource-limits example file.");
    }

    [Fact]
    public async Task Loop_iteration_quota_surfaces_quota_exceeded_and_meters_iterations()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "cmp-0019-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "while",
                  "condition": { "bool": true },
                  "body": [{ "op": "set", "name": "x", "value": { "i32": 1 } }]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(10_000).WithMaxLoopIterations(3).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.True(result.ResourceUsage.LoopIterations >= 3);
    }

    [Fact]
    public async Task Host_call_quota_surfaces_quota_exceeded_and_meters_host_calls()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "cmp-0019-host-calls",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "Emit operational logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "first" }] } },
                { "op": "return", "value": { "call": "log.warn", "args": [{ "string": "second" }] } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().GrantLogging().WithMaxHostCalls(1).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(2, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Exhausted_wall_time_surfaces_the_public_timeout_code()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "cmp-0019-wall-time",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).WithWallTime(TimeSpan.Zero).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
    }

    [Fact]
    public async Task List_shape_quota_surfaces_quota_exceeded()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "cmp-0019-list",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }] }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxListLength(2).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task String_shape_quota_surfaces_quota_exceeded()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "cmp-0019-string",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [{ "op": "return", "value": { "string": "hello" } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxStringLength(4).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
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
            if (File.Exists(Path.Combine(current.FullName, "SafeIR.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
