namespace DotBoxd.Kernels.Benchmarks.Interpreter;

using BenchmarkDotNet.Attributes;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Serialization.Json;

[MemoryDiagnoser]
public class InterpreterExpressionBenchmarks
{
    private SandboxHost _host = null!;
    private ExecutionPlan _plan = null!;

    [Params(100, 10_000)]
    public int Iterations { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await _host.ImportJsonAsync(ModuleJson());
        _plan = await _host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(Iterations * 40L)
                .WithMaxLoopIterations(Iterations + 1)
                .Build());
    }

    [Benchmark]
    public async ValueTask<SandboxExecutionResult> ExecuteArithmeticLoopAsync()
        => await _host.ExecuteAsync(
            _plan,
            "main",
            SandboxValue.FromInt32(Iterations),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

    private static string ModuleJson()
        => """
        {
          "id": "interpreter-expression-benchmark",
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
                        "op": "add",
                        "left": { "var": "total" },
                        "right": {
                          "op": "mul",
                          "left": { "var": "i" },
                          "right": { "i32": 3 }
                        }
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
