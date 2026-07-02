using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Regression;

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
/// The old runnable resource-limit example is no longer maintained. These tests keep the public
/// behavior covered and require the example gap to be documented.
/// </summary>
public sealed class Fix_CMP_0019_Tests
{
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
    public void Removed_resource_limit_sample_is_listed_as_an_example_coverage_gap()
    {
        var gaps = ReadRepositoryText("docs-site/src/content/docs/examples/coverage-gaps.md");

        Assert.Contains("Standalone resource-limit demonstrations", gaps);
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
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
