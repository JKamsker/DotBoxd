using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerGeneratedPackageCompiledTests
{
    [Fact]
    public async Task Generated_should_handle_executes_supported_expression_subset_in_compiled_mode()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount, bool Disabled);

            [Plugin("generated-compiled-expression-subset")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => !e.Disabled && (e.Message != "" || e.Amount == -1);

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
            .WithFuel(100_000)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(1_000)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        await AssertShouldHandleAsync(host, plan, package, "message", 0, disabled: false, expected: true);
        await AssertShouldHandleAsync(host, plan, package, "", -1, disabled: false, expected: true);
        await AssertShouldHandleAsync(host, plan, package, "", 0, disabled: false, expected: false);
        await AssertShouldHandleAsync(host, plan, package, "message", 0, disabled: true, expected: false);
    }

    private static async Task AssertShouldHandleAsync(
        SandboxHost host,
        ExecutionPlan plan,
        PluginPackage package,
        string message,
        int amount,
        bool disabled,
        bool expected)
    {
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input(message, amount, disabled),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(expected, ((BoolValue)result.Value!).Value);
    }

    private static SandboxValue Input(string message, int amount, bool disabled)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(message),
            SandboxValue.FromInt32(amount),
            SandboxValue.FromBool(disabled)
        ]);

    private static string ExecutionFailure(SandboxExecutionResult result)
        => result.Error?.SafeMessage + Environment.NewLine +
           string.Join(Environment.NewLine, result.AuditEvents.Select(e => $"{e.Kind}: {e.ErrorCode} {e.Message}"));
}
