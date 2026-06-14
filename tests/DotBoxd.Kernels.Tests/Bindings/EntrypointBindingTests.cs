using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class EntrypointBindingTests
{
    [Fact]
    public async Task Compiled_single_list_parameter_receives_whole_input()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SingleListParameterJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]);

        var interpreted = await ExecuteAsync(host, plan, input, ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(host, plan, input, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(2, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(2, ((I32Value)compiled.Value!).Value);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Entrypoint_rejects_extra_multi_parameter_inputs(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([
            SandboxValue.FromInt32(1),
            SandboxValue.FromInt32(2),
            SandboxValue.FromInt32(3)
        ]);

        var result = await ExecuteAsync(host, plan, input, mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Entrypoint_rejects_wrong_single_parameter_type(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SingleI32ParameterJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteAsync(host, plan, SandboxValue.FromString("wrong"), mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(
        DotBoxd.Hosting.SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        ExecutionMode mode)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static string SingleListParameterJson()
        => """
        {
          "id": "single-list-entrypoint",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "items", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "list.count", "args": [{ "var": "items" }] }
                }
              ]
            }
          ]
        }
        """;

    private static string SingleI32ParameterJson()
        => """
        {
          "id": "single-i32-entrypoint",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "value", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "var": "value" } }]
            }
          ]
        }
        """;
}
