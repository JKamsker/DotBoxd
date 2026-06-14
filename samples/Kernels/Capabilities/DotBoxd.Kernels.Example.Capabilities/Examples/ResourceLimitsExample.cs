namespace DotBoxd.Kernels.Example.Capabilities;

using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Standalone runtime proof for the non-fuel resource-limit surface. Hosts can configure far more
/// than <c>WithFuel(...)</c>: loop iterations, host calls, wall time, collection shape, and string
/// shape are all public <see cref="SandboxPolicyBuilder"/> knobs. This walkthrough runs small JSON IR
/// modules under intentionally tight limits and prints the public <see cref="SandboxErrorCode"/> plus
/// the matching <see cref="SandboxResourceUsage"/> counters so integrators can recognize a quota or
/// timeout result and read back the usage the runtime metered.
/// </summary>
internal static class ResourceLimitsExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("resource limits walkthrough:");

        await LoopIterationLimitAsync();
        await HostCallLimitAsync();
        await WallTimeTimeoutAsync();
        await ListShapeLimitAsync();
        await StringShapeLimitAsync();
    }

    private static async Task LoopIterationLimitAsync()
    {
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "resource-limits-loop",
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

        // The loop never terminates on its own; WithMaxLoopIterations stops it deterministically.
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxLoopIterations(3)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine(
            $"  loop iterations: error={result.Error?.Code}, loopIterations={result.ResourceUsage.LoopIterations}");
    }

    private static async Task HostCallLimitAsync()
    {
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

        // Every binding invocation is a host call; this module makes two while the budget allows one.
        var module = await host.ImportJsonAsync("""
        {
          "id": "resource-limits-host-calls",
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

        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithMaxHostCalls(1)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine(
            $"  host calls: error={result.Error?.Code}, hostCalls={result.ResourceUsage.HostCalls}");
    }

    private static async Task WallTimeTimeoutAsync()
    {
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "resource-limits-wall-time",
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

        // An exhausted wall-time budget surfaces the documented Timeout code, not QuotaExceeded.
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithWallTime(TimeSpan.Zero)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine($"  wall time: error={result.Error?.Code}");
    }

    private static async Task ListShapeLimitAsync()
    {
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "resource-limits-list",
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

        // A three-element list violates the two-element shape limit and is rejected by quota.
        var policy = SandboxPolicyBuilder.Create()
            .WithMaxListLength(2)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine($"  list shape: error={result.Error?.Code}");
    }

    private static async Task StringShapeLimitAsync()
    {
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync("""
        {
          "id": "resource-limits-string",
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

        // A five-character literal exceeds the four-character shape limit and is rejected by quota.
        var policy = SandboxPolicyBuilder.Create()
            .WithMaxStringLength(4)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Console.WriteLine(
            $"  string shape: error={result.Error?.Code}, stringBytes={result.ResourceUsage.StringBytes}");
    }
}
