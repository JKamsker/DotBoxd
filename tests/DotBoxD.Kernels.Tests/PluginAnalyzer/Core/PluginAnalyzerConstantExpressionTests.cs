using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerConstantExpressionTests
{
    [Fact]
    public async Task Generated_package_lowers_compile_time_constants_in_should_handle_and_handle()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(
                string TargetId,
                string DamageType,
                string Message,
                int Amount,
                long Sequence,
                double Ratio);

            [Plugin("generated-constant-expressions")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                private const bool Enabled = true;
                private const int MinDamage = 100;
                private const long RequiredSequence = 7L;
                private const double RequiredRatio = 1.5D;
                private const string RequiredType = "fire";
                private const string Prefix = "hit:";

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => Enabled &&
                       e.Amount >= MinDamage &&
                       e.Sequence == RequiredSequence &&
                       e.Ratio == RequiredRatio &&
                       e.DamageType == RequiredType;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, Prefix + e.Message);
            }
            """);

        await AssertCompiledShouldHandleAsync(package, matching: true);
        await AssertCompiledShouldHandleAsync(package, matching: false);

        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);
        var adapter = new ConstantDamageEventAdapter();

        await kernel.HandleAsync(adapter, MatchingEvent());

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("hit:matched", message.Message);
    }

    private static async Task AssertCompiledShouldHandleAsync(PluginPackage package, bool matching)
    {
        var host = SandboxHost.Create(builder =>
        {
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
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input(matching),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(matching, ((BoolValue)result.Value!).Value);
    }

    private static SandboxValue Input(bool matching)
    {
        var e = matching
            ? MatchingEvent()
            : MatchingEvent() with { Amount = 99 };
        return SandboxValue.FromList([
            SandboxValue.FromString(e.TargetId),
            SandboxValue.FromString(e.DamageType),
            SandboxValue.FromString(e.Message),
            SandboxValue.FromInt32(e.Amount),
            SandboxValue.FromInt64(e.Sequence),
            SandboxValue.FromDouble(e.Ratio)
        ]);
    }

    private static ConstantDamageEvent MatchingEvent()
        => new("player-1", "fire", "matched", 100, 7L, 1.5D);

    private static string ExecutionFailure(SandboxExecutionResult result)
        => result.Error?.SafeMessage + Environment.NewLine +
           string.Join(Environment.NewLine, result.AuditEvents.Select(e => $"{e.Kind}: {e.ErrorCode} {e.Message}"));

    private sealed record ConstantDamageEvent(
        string TargetId,
        string DamageType,
        string Message,
        int Amount,
        long Sequence,
        double Ratio);

    private sealed class ConstantDamageEventAdapter : IPluginEventAdapter<ConstantDamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_DamageType", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_Sequence", SandboxType.I64),
            new("e_Ratio", SandboxType.F64)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ConstantDamageEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromInt64(e.Sequence),
                SandboxValue.FromDouble(e.Ratio)
            ];
    }
}
