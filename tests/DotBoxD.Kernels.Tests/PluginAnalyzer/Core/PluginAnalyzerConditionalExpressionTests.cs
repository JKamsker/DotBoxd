using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerConditionalExpressionTests
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
    public async Task Generated_should_handle_executes_selected_conditional_branch(
        ExecutionMode mode)
    {
        var disabled = await ExecuteShouldHandleAsync(amount: 0, enabled: false, mode);
        var enabled = await ExecuteShouldHandleAsync(amount: 10, enabled: true, mode);

        AssertBool(disabled, expected: true, mode);
        AssertBool(enabled, expected: true, mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_does_not_evaluate_unselected_conditional_branch(
        ExecutionMode mode)
    {
        var skippedFault = await ExecuteShouldHandleAsync(amount: 0, enabled: false, mode);
        var selectedFault = await ExecuteShouldHandleAsync(amount: 0, enabled: true, mode);

        AssertBool(skippedFault, expected: true, mode);
        AssertInvalidInput(selectedFault, mode);
    }

    private static async Task<SandboxExecutionResult> ExecuteShouldHandleAsync(
        int amount,
        bool enabled,
        ExecutionMode mode)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount, bool Enabled);

            [Plugin("generated-conditional-expression")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Enabled ? 100 / e.Amount > 0 : e.Amount == 0;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);
        var host = SandboxHost.Create(builder =>
        {
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

        return await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input(amount, enabled),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static SandboxValue Input(int amount, bool enabled)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString("matched"),
            SandboxValue.FromInt32(amount),
            SandboxValue.FromBool(enabled)
        ]);

    private static void AssertBool(SandboxExecutionResult result, bool expected, ExecutionMode mode)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static void AssertInvalidInput(SandboxExecutionResult result, ExecutionMode mode)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }
}
