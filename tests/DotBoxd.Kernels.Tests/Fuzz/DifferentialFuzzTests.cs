using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotBoxd.Kernels.Tests;

public sealed class DifferentialFuzzTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Parameters = ["a", "b", "c"];

    [Fact]
    public async Task Pure_i32_json_fuzz_cases_match_interpreter_and_compiler()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var random = new Random(0x51AFE);

        for (var index = 0; index < 40; index++) {
            var expression = Expression(random, depth: 4);
            var json = ModuleJson(index, expression);
            var module = await host.ImportJsonAsync(json);
            var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());
            var input = Input(random);

            var interpreted = await ExecuteAsync(host, plan, input, ExecutionMode.Interpreted);
            var compiled = await ExecuteAsync(host, plan, input, ExecutionMode.Compiled);

            AssertEqual(index, json, interpreted, compiled);
        }
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(
        Hosting.SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        ExecutionMode mode)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static void AssertEqual(
        int index,
        string json,
        SandboxExecutionResult interpreted,
        SandboxExecutionResult compiled)
    {
        Assert.True(interpreted.Succeeded, $"case {index} interpreted failed: {json}");
        Assert.True(compiled.Succeeded, $"case {index} compiled failed: {compiled.Error?.SafeMessage}; {json}");
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.True(
            interpreted.Value is I32Value expected &&
            compiled.Value is I32Value actual &&
            expected.Value == actual.Value,
            $"case {index} result mismatch: {json}");
        Assert.InRange(
            compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed,
            0,
            8);
        Assert.Equal(0, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(0, compiled.ResourceUsage.HostCalls);
        Assert.Equal(0, interpreted.ResourceUsage.FileBytesRead);
        Assert.Equal(0, compiled.ResourceUsage.FileBytesRead);
        Assert.Equal(0, interpreted.ResourceUsage.NetworkBytesRead);
        Assert.Equal(0, compiled.ResourceUsage.NetworkBytesRead);
        Assert.Contains(interpreted.AuditEvents, e => e.Kind == "RunSummary" && e.Success);
        Assert.Contains(compiled.AuditEvents, e => e.Kind == "RunSummary" && e.Success);
    }

    private static SandboxValue Input(Random random)
        => SandboxValue.FromList([
            SandboxValue.FromInt32(random.Next(-5, 6)),
            SandboxValue.FromInt32(random.Next(-5, 6)),
            SandboxValue.FromInt32(random.Next(-5, 6))
        ]);

    private static JsonObject Expression(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0) {
            return random.Next(2) == 0
                ? new JsonObject { ["i32"] = random.Next(-3, 4) }
                : new JsonObject { ["var"] = Parameters[random.Next(Parameters.Length)] };
        }

        return new JsonObject {
            ["op"] = Operator(random),
            ["left"] = Expression(random, depth - 1),
            ["right"] = Expression(random, depth - 1)
        };
    }

    private static string Operator(Random random)
        => random.Next(3) switch {
            0 => "add",
            1 => "sub",
            _ => "mul"
        };

    private static string ModuleJson(int index, JsonObject expression)
        => new JsonObject {
            ["id"] = $"differential-fuzz-{index}",
            ["version"] = "1.0.0",
            ["functions"] = new JsonArray {
                new JsonObject {
                    ["id"] = "main",
                    ["visibility"] = "entrypoint",
                    ["parameters"] = new JsonArray {
                        Parameter("a"),
                        Parameter("b"),
                        Parameter("c")
                    },
                    ["returnType"] = "I32",
                    ["body"] = new JsonArray {
                        new JsonObject {
                            ["op"] = "return",
                            ["value"] = expression
                        }
                    }
                }
            }
        }.ToJsonString(JsonOptions);

    private static JsonObject Parameter(string name)
        => new() {
            ["name"] = name,
            ["type"] = "I32"
        };
}
