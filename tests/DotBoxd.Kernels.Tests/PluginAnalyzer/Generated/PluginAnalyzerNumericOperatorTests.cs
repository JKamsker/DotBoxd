using DotBoxd.Hosting;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAnalyzerNumericOperatorTests
{
    private const int FuelLimit = 100_000;
    private const int HostCallLimit = 1_000;

    [Fact]
    public async Task Generated_should_handle_lowers_i64_and_f64_numeric_operators_in_compiled_mode()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Sequence, double Ratio, int Amount);

            [Plugin("generated-numeric-operators")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                private const long SequenceOffset = 2L;
                private const double RatioScale = 2D;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Sequence + SequenceOffset >= 9L &&
                       -e.Sequence < 0L &&
                       e.Ratio * RatioScale > 2.5D &&
                       e.Amount + 1 > 1;

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
            .GrantHostMessageWrite()
            .WithFuel(FuelLimit)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(HostCallLimit)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        await AssertShouldHandleAsync(host, plan, package, Input(7L, 1.5D, 1), expected: true);
        await AssertShouldHandleAsync(host, plan, package, Input(6L, 1.5D, 1), expected: false);
        await AssertShouldHandleAsync(host, plan, package, Input(7L, 1.0D, 1), expected: false);
        await AssertShouldHandleAsync(host, plan, package, Input(7L, 1.5D, 0), expected: false);
    }

    [Fact]
    public async Task Generated_should_handle_promotes_numeric_constants_in_compiled_mode()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Sequence, double Ratio, int Amount);

            [Plugin("generated-promoted-numeric-constants")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Sequence + 1 > 7 &&
                       e.Ratio * 2 >= 3 &&
                       e.Amount > 0;

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
            .GrantHostMessageWrite()
            .WithFuel(FuelLimit)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(HostCallLimit)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        await AssertShouldHandleAsync(host, plan, package, Input(7L, 1.5D, 1), expected: true);
        await AssertShouldHandleAsync(host, plan, package, Input(6L, 1.5D, 1), expected: false);
        await AssertShouldHandleAsync(host, plan, package, Input(7L, 1.0D, 1), expected: false);
    }

    private static async Task AssertShouldHandleAsync(
        SandboxHost host,
        ExecutionPlan plan,
        PluginPackage package,
        SandboxValue input,
        bool expected)
    {
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
    }

    private static SandboxValue Input(long sequence, double ratio, int amount)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString("matched"),
            SandboxValue.FromInt64(sequence),
            SandboxValue.FromDouble(ratio),
            SandboxValue.FromInt32(amount)
        ]);

    private static string ExecutionFailure(SandboxExecutionResult result)
        => result.Error?.SafeMessage + Environment.NewLine +
           string.Join(Environment.NewLine, result.AuditEvents.Select(e => $"{e.Kind}: {e.ErrorCode} {e.Message}"));
}
