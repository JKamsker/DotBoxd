using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class EventReadRuntimeCapabilityTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task Runtime_rejects_gated_event_parameter_when_package_omits_event_read_metadata()
    {
        using var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var kernel = await server.InstallAsync(GatedEventPackage(includeMetadata: false));
        var adapter = new GatedRuntimeEventAdapter();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            () => kernel.ShouldHandleAsync(adapter, new GatedRuntimeEvent("target-1", 10)).AsTask());

        Assert.Equal(0, adapter.MaterializedValues);
        Assert.Contains(ex.Diagnostics, diagnostic =>
            diagnostic.Code == "DBXK044" &&
            diagnostic.Message.Contains("event.read.health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runtime_accepts_gated_event_parameter_when_event_read_metadata_is_granted()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("event.read.health", new { }, SandboxEffect.None)
            .WithFuel(10_000)
            .WithMaxHostCalls(10)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
        using var server = PluginServer.Create(defaultPolicy: policy);
        var kernel = await server.InstallAsync(GatedEventPackage(includeMetadata: true), policy);
        var adapter = new GatedRuntimeEventAdapter();

        var handled = await kernel.ShouldHandleAsync(adapter, new GatedRuntimeEvent("target-1", 10));

        Assert.True(handled);
        Assert.Equal(1, adapter.MaterializedValues);
    }

    private static PluginPackage GatedEventPackage(bool includeMetadata)
    {
        var metadata = new Dictionary<string, string>
        {
            ["pluginId"] = "gated-runtime",
            ["kernel"] = "GatedRuntimeKernel"
        };
        if (includeMetadata)
        {
            metadata["requiredCapabilities"] = "event.read.health";
        }

        return PluginPackage.Create(
            new PluginManifest(
                "gated-runtime",
                "IEventKernel<GatedRuntimeEvent>",
                ExecutionMode.Interpreted,
                [nameof(SandboxEffect.Cpu)],
                [],
                [new HookSubscriptionManifest(nameof(GatedRuntimeEvent), "GatedRuntimeKernel")])
            {
                RequiredCapabilities = includeMetadata ? ["event.read.health"] : []
            },
            new SandboxModule(
                "gated-runtime",
                SemVersion.One,
                SemVersion.One,
                [],
                [ShouldHandle(), Handle()],
                metadata));
    }

    private static SandboxFunction ShouldHandle()
        => new(
            "ShouldHandle",
            true,
            EventParameters(),
            SandboxType.Bool,
            [
                new ReturnStatement(
                    new BinaryExpression(
                        new VariableExpression("e_Health", Span),
                        ">",
                        new LiteralExpression(SandboxValue.FromInt32(0), Span),
                        Span),
                    Span)
            ]);

    private static SandboxFunction Handle()
        => new(
            "Handle",
            true,
            EventParameters(),
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

    private static Parameter[] EventParameters() =>
    [
        new("e_TargetId", SandboxType.String),
        new("e_Health", SandboxType.I32)
    ];

    private sealed record GatedRuntimeEvent(
        string TargetId,
        [property: Capability("event.read.health")] int Health);

    private sealed class GatedRuntimeEventAdapter : IPluginEventAdapter<GatedRuntimeEvent>
    {
        public int MaterializedValues { get; private set; }

        public string EventName => nameof(GatedRuntimeEvent);

        public IReadOnlyList<Parameter> Parameters => EventParameters();

        public IReadOnlyList<SandboxValue> ToSandboxValues(GatedRuntimeEvent e)
        {
            MaterializedValues++;
            return [SandboxValue.FromString(e.TargetId), SandboxValue.FromInt32(e.Health)];
        }
    }
}
