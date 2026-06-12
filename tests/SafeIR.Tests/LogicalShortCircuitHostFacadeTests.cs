using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed partial class LogicalShortCircuitTests
{
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Cheap_first_does_not_reorder_pure_host_facade_operands(ExecutionMode mode)
    {
        var calls = 0;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(BoolBinding(
                () => {
                    calls++;
                    return true;
                },
                BindingSafety.PureHostFacade));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ParseJsonAsync(ModuleJson(
            """{ "op": "and", "left": { "call": "test.bool", "args": [] }, "right": { "bool": false } }"""));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }
}
