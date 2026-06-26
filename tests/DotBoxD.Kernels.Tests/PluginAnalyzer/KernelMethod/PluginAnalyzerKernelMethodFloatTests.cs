using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

/// <summary>
/// The <c>[KernelMethod]</c> signature gate treats <c>System.Single</c> (float) as <c>Double</c>
/// (see <c>SandboxTypeSourceEmitter.ManifestTag</c>), so a <c>float</c> helper passes the param/return gates.
/// These tests pin that the BODY lowering agrees: a float literal or float arithmetic inside an inlined
/// <c>[KernelMethod]</c> body lowers to F64 and runs with the correct numeric result, instead of dying with a
/// misleading "Unsupported plugin constant expression" body error. A pure pass-through float (no literal/arith)
/// is also covered as a guard.
/// </summary>
public sealed class PluginAnalyzerKernelMethodFloatTests
{
    private const string FloatHalfKernelSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record SpeedEvent(string TargetId, string Message, float Speed, float Limit);

        [Plugin("inlined-float-half")]
        public sealed partial class FloatHalfKernel : IEventKernel<SpeedEvent>
        {
            public bool ShouldHandle(SpeedEvent e, HookContext ctx)
                => Half(e.Speed) <= e.Limit;

            public void Handle(SpeedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);

            [KernelMethod]
            public static float Half(float x) => x / 2f;
        }
        """;

    private const string FloatPassThroughKernelSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record SpeedEvent(string TargetId, string Message, float Speed, float Limit);

        [Plugin("inlined-float-passthrough")]
        public sealed partial class FloatPassThroughKernel : IEventKernel<SpeedEvent>
        {
            public bool ShouldHandle(SpeedEvent e, HookContext ctx)
                => Identity(e.Speed) <= e.Limit;

            public void Handle(SpeedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);

            [KernelMethod]
            public static float Identity(float x) => x;
        }
        """;

    [Fact]
    public async Task KernelMethod_float_literal_and_arithmetic_in_body_lower_and_run()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            FloatHalfKernelSource,
            "Sample.FloatHalfPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            new InMemoryPluginMessageSink(),
            defaultPolicy: SandboxedPolicy());
        var kernel = await server.InstallAsync(package);
        var adapter = new SpeedAdapter();

        // 10 / 2 = 5 <= 6 → handled.
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, new SpeedSample("p", "calm", 10f, 6f)));
        // 20 / 2 = 10 <= 6 is false → not handled.
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, new SpeedSample("p", "calm", 20f, 6f)));
    }

    [Fact]
    public async Task KernelMethod_pass_through_float_argument_runs()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            FloatPassThroughKernelSource,
            "Sample.FloatPassThroughPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            new InMemoryPluginMessageSink(),
            defaultPolicy: SandboxedPolicy());
        var kernel = await server.InstallAsync(package);
        var adapter = new SpeedAdapter();

        // 4 <= 6 → handled.
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, new SpeedSample("p", "calm", 4f, 6f)));
        // 9 <= 6 is false → not handled.
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, new SpeedSample("p", "calm", 9f, 6f)));
    }

    private static SandboxPolicy SandboxedPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private sealed record SpeedSample(string TargetId, string Message, float Speed, float Limit);

    private sealed class SpeedAdapter : IPluginEventAdapter<SpeedSample>
    {
        public string EventName => "SpeedEvent";

        public IReadOnlyList<Parameter> Parameters { get; } =
        [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_Speed", SandboxType.F64),
            new("e_Limit", SandboxType.F64)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(SpeedSample e)
            =>
            [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromDouble(e.Speed),
                SandboxValue.FromDouble(e.Limit)
            ];
    }
}
