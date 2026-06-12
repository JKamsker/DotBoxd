using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerBlockBodyTests
{
    private const int FuelLimit = 100_000;
    private const int HostCallLimit = 1_000;

    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Fact]
    public void Generated_should_handle_lowers_if_else_return_body_to_ir_branch()
    {
        var package = CreatePackage();
        var shouldHandle = package.Module.Functions.Single(f => f.Id == package.Entrypoints.ShouldHandle);

        var branch = Assert.IsType<IfStatement>(Assert.Single(shouldHandle.Body));
        Assert.NotEmpty(branch.Then);
        Assert.NotEmpty(branch.Else);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_executes_selected_if_else_return_branch(
        ExecutionMode mode)
    {
        var skippedFault = await ExecuteShouldHandleAsync(amount: 0, enabled: false, mode);
        var selectedFault = await ExecuteShouldHandleAsync(amount: 0, enabled: true, mode);
        var selectedMatch = await ExecuteShouldHandleAsync(amount: 10, enabled: true, mode);

        AssertBool(skippedFault, expected: true, mode);
        AssertInvalidInput(selectedFault, mode);
        AssertBool(selectedMatch, expected: true, mode);
    }

    private static async Task<SandboxExecutionResult> ExecuteShouldHandleAsync(
        int amount,
        bool enabled,
        ExecutionMode mode)
    {
        var package = CreatePackage();
        var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .GrantGameMessageWrite()
            .WithFuel(FuelLimit)
            .WithMaxHostCalls(HostCallLimit)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        return await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input(amount, enabled),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static PluginPackage CreatePackage()
        => PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount, bool Enabled);

            [GamePlugin("generated-block-body")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                {
                    if (e.Enabled)
                    {
                        return 100 / e.Amount > 0;
                    }
                    else
                    {
                        return e.Amount == 0;
                    }
                }

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

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
