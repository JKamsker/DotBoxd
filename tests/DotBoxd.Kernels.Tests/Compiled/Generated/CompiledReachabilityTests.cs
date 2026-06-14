namespace DotBoxd.Kernels.Tests;

public sealed class CompiledReachabilityTests
{
    [Fact]
    public async Task Compiled_mode_emits_only_functions_reachable_from_entrypoint()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(UnreferencedUnsupportedHelperJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(5_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static string UnreferencedUnsupportedHelperJson()
        => """
        {
          "id": "compiled-unreferenced-helper",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            },
            {
              "id": "unused",
              "visibility": "private",
              "parameters": [],
              "returnType": "Unit",
              "body": [{ "op": "return", "value": { "unit": true } }]
            }
          ]
        }
        """;
}
