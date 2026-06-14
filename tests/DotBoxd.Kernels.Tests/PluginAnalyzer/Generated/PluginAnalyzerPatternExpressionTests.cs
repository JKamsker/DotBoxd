using DotBoxd.Hosting;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAnalyzerPatternExpressionTests
{
    private const int FuelLimit = 100_000;
    private const int HostCallLimit = 1_000;

    [Fact]
    public async Task Generated_should_handle_executes_supported_patterns_in_compiled_mode()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, long Sequence, double Ratio);

            [Plugin("generated-patterns")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Message is "fire" &&
                       e.Sequence is > 0 &&
                       e.Ratio is not <= 1;

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

        await AssertShouldHandleAsync(host, plan, package, Input("fire", 1L, 1.5D), expected: true);
        await AssertShouldHandleAsync(host, plan, package, Input("ice", 1L, 1.5D), expected: false);
        await AssertShouldHandleAsync(host, plan, package, Input("fire", 0L, 1.5D), expected: false);
        await AssertShouldHandleAsync(host, plan, package, Input("fire", 1L, 1.0D), expected: false);
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

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
    }

    private static SandboxValue Input(string message, long sequence, double ratio)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(message),
            SandboxValue.FromInt64(sequence),
            SandboxValue.FromDouble(ratio)
        ]);
}
