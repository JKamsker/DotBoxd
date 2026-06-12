using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerStringPrimitiveTests
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
    public async Task Generated_should_handle_executes_string_length_substring_and_equals(
        ExecutionMode mode)
    {
        var package = CreatePackage();
        Assert.Contains("Alloc", package.Manifest.Effects);
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

        await AssertShouldHandleAsync(host, plan, package, Input("fire bolt"), expected: true, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("ice bolt"), expected: false, mode);
        await AssertShouldHandleAsync(host, plan, package, Input("fir"), expected: false, mode);
    }

    private static PluginPackage CreatePackage()
        => PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record StringPrimitiveEvent(string TargetId, string Message);

            [GamePlugin("generated-string-primitives")]
            public sealed partial class StringPrimitiveKernel : IEventKernel<StringPrimitiveEvent>
            {
                public bool ShouldHandle(StringPrimitiveEvent e, HookContext ctx)
                    => e.Message.Length >= 4 &&
                       e.Message.Substring(startIndex: 0, length: 4).Equals("fire");

                public void Handle(StringPrimitiveEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "Sample.StringPrimitivePluginPackage");

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

    private static SandboxValue Input(string message)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(message)
        ]);
}
