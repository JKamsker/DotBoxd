using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerGeneratedPackageTests
{
    [Fact]
    public async Task Generated_package_installs_and_executes_lowered_ir()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using System.ComponentModel.DataAnnotations;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(
                string TargetId,
                string Message,
                string DamageType,
                int Amount,
                long Sequence,
                double Ratio);

            [Plugin("generated-runtime")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public bool Enabled { get; set; } = true;

                [LiveSetting]
                public string DamageType { get; set; } = "fire";

                [LiveSetting]
                [Range(0, 200)]
                public int MinDamage { get; set; } = 100;

                [LiveSetting]
                [Range(typeof(long), "1", "9")]
                public long Sequence { get; set; } = 7L;

                [LiveSetting]
                [Range(0.5D, 2.5D)]
                public double Ratio { get; set; } = 1.5D;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => Enabled &&
                       e.DamageType == DamageType &&
                       e.Amount >= MinDamage &&
                       e.Sequence == Sequence &&
                       e.Ratio == Ratio;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Equal("generated-runtime", package.Manifest.PluginId);
        Assert.Equal(["Cpu", "HostStateWrite", "Concurrency", "Audit"], package.Manifest.Effects);
        Assert.Collection(
            package.Manifest.LiveSettings,
            setting => AssertLiveSetting(setting, "Enabled", "bool", true),
            setting => AssertLiveSetting(setting, "DamageType", "string", "fire"),
            setting => AssertLiveSetting(setting, "MinDamage", "int", 100, 0, 200),
            setting => AssertLiveSetting(setting, "Sequence", "long", 7L, 1L, 9L),
            setting => AssertLiveSetting(setting, "Ratio", "double", 1.5D, 0.5D, 2.5D));

        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);
        var adapter = new GeneratedDamageEventAdapter();

        var matching = new GeneratedDamageEvent("player-1", "matched", "fire", 150, 7L, 1.5D);
        var rejected = matching with { Amount = 99 };

        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, rejected));
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, matching));

        await kernel.HandleAsync(adapter, matching);

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("matched", message.Message);
    }

    [Fact]
    public async Task Generated_package_executes_supported_i32_operator_subset()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(
                string TargetId,
                string Message,
                string DamageType,
                int Amount,
                long Sequence,
                double Ratio);

            [Plugin("generated-i32-operators")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int Zero { get; set; } = 0;

                [LiveSetting]
                public int Divisor { get; set; } = 1;

                [LiveSetting]
                public int Modulo { get; set; } = 3;

                [LiveSetting]
                public int Ceiling { get; set; } = 10;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => (((e.Amount + Zero) * Zero / Divisor) % Modulo) < Zero ||
                       (e.Amount <= Ceiling - Zero && e.Amount > -1);

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(package);
        var adapter = new GeneratedDamageEventAdapter();

        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, EventWithAmount(0)));
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, EventWithAmount(10)));
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, EventWithAmount(-1)));
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, EventWithAmount(11)));
    }

    [Fact]
    public async Task Generated_package_executes_i64_literal_lowering_in_compiled_mode()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(
                string TargetId,
                string Message,
                string DamageType,
                int Amount,
                long Sequence,
                double Ratio);

            [Plugin("generated-i64-literal")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Sequence == 5L;

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

        var matching = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            InputWithSequence(5L),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
        var rejected = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            InputWithSequence(4L),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(matching.Succeeded, ExecutionFailure(matching));
        Assert.True(rejected.Succeeded, ExecutionFailure(rejected));
        Assert.Equal(ExecutionMode.Compiled, matching.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, rejected.ActualMode);
        Assert.True(((BoolValue)matching.Value!).Value);
        Assert.False(((BoolValue)rejected.Value!).Value);
    }

    private static void AssertLiveSetting(
        LiveSettingDefinition actual,
        string name,
        string type,
        object expectedDefault,
        object? expectedMin = null,
        object? expectedMax = null)
    {
        Assert.Equal(name, actual.Name);
        Assert.Equal(type, actual.Type);
        Assert.Equal(expectedDefault, actual.DefaultValue);
        Assert.Equal(expectedMin, actual.Min);
        Assert.Equal(expectedMax, actual.Max);
    }

    private static GeneratedDamageEvent EventWithAmount(int amount)
        => new("player-1", "matched", "fire", amount, 7L, 1.5D);

    private static string ExecutionFailure(SandboxExecutionResult result)
        => result.Error?.SafeMessage + Environment.NewLine +
           string.Join(Environment.NewLine, result.AuditEvents.Select(e => $"{e.Kind}: {e.ErrorCode} {e.Message}"));

    private static SandboxValue InputWithSequence(long sequence)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString("matched"),
            SandboxValue.FromString("fire"),
            SandboxValue.FromInt32(0),
            SandboxValue.FromInt64(sequence),
            SandboxValue.FromDouble(1.5D)
        ]);

    private sealed record GeneratedDamageEvent(
        string TargetId, string Message, string DamageType, int Amount, long Sequence, double Ratio);

    private sealed class GeneratedDamageEventAdapter : IPluginEventAdapter<GeneratedDamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_Sequence", SandboxType.I64),
            new("e_Ratio", SandboxType.F64)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(GeneratedDamageEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromInt64(e.Sequence),
                SandboxValue.FromDouble(e.Ratio)
            ];
    }
}
