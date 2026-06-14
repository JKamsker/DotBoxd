using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerInvocationExpressionTests
{
    private const int FuelLimit = 100_000;
    private const int HostCallLimit = 1_000;

    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_executes_supported_instance_equals_calls(
        ExecutionMode mode)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record EqualsEvent(string TargetId, string Message, int Amount, bool Enabled);

            [Plugin("generated-instance-equals")]
            public sealed partial class EqualsKernel : IEventKernel<EqualsEvent>
            {
                public bool ShouldHandle(EqualsEvent e, HookContext ctx)
                    => e.Message.Equals("fire") &&
                       e.Amount.Equals(7) &&
                       e.Enabled.Equals(true);

                public void Handle(EqualsEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "Sample.EqualsPluginPackage");
        var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(FuelLimit)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(HostCallLimit)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        await AssertShouldHandleAsync(host, plan, package, Input("fire", 7, enabled: true), expected: true, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("ice", 7, enabled: true), expected: false, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("fire", 8, enabled: true), expected: false, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("fire", 7, enabled: false), expected: false, mode);
    }

    private static async Task AssertShouldHandleAsync(
        SandboxHost host,
        ExecutionPlan plan,
        PluginPackage package,
        SandboxValue input,
        bool expected,
        ExecutionMode mode)
    {
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static SandboxValue Input(string message, int amount, bool enabled)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(message),
            SandboxValue.FromInt32(amount),
            SandboxValue.FromBool(enabled)
        ]);
}
