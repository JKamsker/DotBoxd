using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class CompiledFuelAccountingTests
{
    [Fact]
    public async Task Compiled_expression_fuel_accounts_for_runtime_type_check()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ExpressionModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await ExecuteAsync(host, plan, ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(host, plan, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);

        // Compiled mode charges exactly one fuel unit the interpreter does not: the return-value runtime
        // type check. The unboxed fast path's scalar box/unbox coercions are fuel-transparent, so this delta
        // is unchanged by the unboxing optimization.
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Compiled_expression_fuel_enforces_quota()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ExpressionModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(12).Build());

        var result = await ExecuteAsync(host, plan, ExecutionMode.Compiled);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_i32_loop_fast_path_preserves_successful_fuel_accounting()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(I32LoopModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await ExecuteAsync(host, plan, ExecutionMode.Interpreted, SandboxValue.FromInt32(8));
        var compiled = await ExecuteAsync(host, plan, ExecutionMode.Compiled, SandboxValue.FromInt32(8));

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(
        DotBoxd.Hosting.SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(
        DotBoxd.Hosting.SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static string ExpressionModuleJson()
        => """
        {
          "id": "compiled-expression-fuel",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": {
                      "op": "mul",
                      "left": {
                        "op": "add",
                        "left": { "i32": 1 },
                        "right": { "i32": 2 }
                      },
                      "right": {
                        "op": "sub",
                        "left": { "i32": 9 },
                        "right": { "i32": 3 }
                      }
                    },
                    "right": {
                      "op": "div",
                      "left": { "i32": 8 },
                      "right": { "i32": 2 }
                    }
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string I32LoopModuleJson()
        => """
        {
          "id": "compiled-i32-loop-fuel",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [
                    {
                      "op": "set",
                      "name": "total",
                      "value": {
                        "op": "rem",
                        "left": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } },
                        "right": { "i32": 1000003 }
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
