namespace SafeIR.Tests;

public sealed class LoopIterationLimitTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Loop_iteration_limit_is_enforced(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(InfiniteLoopModule());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxLoopIterations(3)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(4, result.ResourceUsage.LoopIterations);
    }

    private static string InfiniteLoopModule()
        => """
        {
          "id": "loop-iteration-limit",
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
                  "body": [
                    { "op": "set", "name": "x", "value": { "i32": 1 } }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;
}
