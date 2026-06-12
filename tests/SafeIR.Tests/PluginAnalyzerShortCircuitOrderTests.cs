using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerShortCircuitOrderTests
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
    public async Task Generated_should_handle_evaluates_left_operand_before_short_circuiting_right(
        ExecutionMode mode)
    {
        var andResult = await ExecuteShouldHandleAsync(
            "100 / e.Amount > 0 && e.Enabled",
            amount: 0,
            enabled: false,
            mode: mode);
        var orResult = await ExecuteShouldHandleAsync(
            "100 / e.Amount > 0 || e.Enabled",
            amount: 0,
            enabled: true,
            mode: mode);

        AssertInvalidInput(andResult, mode);
        AssertInvalidInput(orResult, mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_skips_right_operand_after_short_circuit(
        ExecutionMode mode)
    {
        var andResult = await ExecuteShouldHandleAsync(
            "e.Enabled && 100 / e.Amount > 0",
            amount: 0,
            enabled: false,
            mode: mode);
        var orResult = await ExecuteShouldHandleAsync(
            "e.Enabled || 100 / e.Amount > 0",
            amount: 0,
            enabled: true,
            mode: mode);

        AssertBool(andResult, expected: false, mode);
        AssertBool(orResult, expected: true, mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_evaluates_right_operand_for_eager_bool_operators(
        ExecutionMode mode)
    {
        var andResult = await ExecuteShouldHandleAsync(
            "e.Enabled & 100 / e.Amount > 0",
            amount: 0,
            enabled: false,
            mode: mode);
        var orResult = await ExecuteShouldHandleAsync(
            "e.Enabled | 100 / e.Amount > 0",
            amount: 0,
            enabled: true,
            mode: mode);
        var xorResult = await ExecuteShouldHandleAsync(
            "e.Enabled ^ 100 / e.Amount > 0",
            amount: 0,
            enabled: false,
            mode: mode);

        AssertInvalidInput(andResult, mode);
        AssertInvalidInput(orResult, mode);
        AssertInvalidInput(xorResult, mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_should_handle_returns_eager_bool_operator_results(
        ExecutionMode mode)
    {
        var andResult = await ExecuteShouldHandleAsync(
            "e.Enabled & e.Amount > 0",
            amount: 1,
            enabled: true,
            mode: mode);
        var orResult = await ExecuteShouldHandleAsync(
            "e.Enabled | e.Amount > 0",
            amount: 0,
            enabled: false,
            mode: mode);
        var xorResult = await ExecuteShouldHandleAsync(
            "e.Enabled ^ e.Amount > 0",
            amount: 0,
            enabled: true,
            mode: mode);

        AssertBool(andResult, expected: true, mode);
        AssertBool(orResult, expected: false, mode);
        AssertBool(xorResult, expected: true, mode);
    }

    private static async Task<SandboxExecutionResult> ExecuteShouldHandleAsync(
        string expression,
        int amount,
        bool enabled,
        ExecutionMode mode)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create($$"""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount, bool Enabled);

            [GamePlugin("generated-short-circuit-order")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => {{expression}};

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);
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

    private static SandboxValue Input(int amount, bool enabled)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString("matched"),
            SandboxValue.FromInt32(amount),
            SandboxValue.FromBool(enabled)
        ]);

    private static void AssertInvalidInput(SandboxExecutionResult result, ExecutionMode mode)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static void AssertBool(SandboxExecutionResult result, bool expected, ExecutionMode mode)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }
}
