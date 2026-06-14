using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed partial class LogicalShortCircuitTests
{
    [Fact]
    public async Task Shadowed_binding_name_uses_function_effects_for_reordering()
    {
        var calls = 0;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(BoolBinding(() => false));
            builder.AddBinding(EffectfulBoolBinding(() => calls++));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ShadowedBindingModuleJson());
        var plan = await host.PrepareAsync(module, GameReadPolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
    }

    private static string ShadowedBindingModuleJson()
        => """
        {
          "id": "logical-short-circuit-shadow",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Bool",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "and",
                    "left": { "call": "test.bool", "args": [] },
                    "right": { "bool": false }
                  }
                }
              ]
            },
            {
              "id": "test.bool",
              "visibility": "private",
              "parameters": [],
              "returnType": "Bool",
              "body": [
                { "op": "return", "value": { "call": "test.effectfulBool", "args": [] } }
              ]
            }
          ]
        }
        """;
}
