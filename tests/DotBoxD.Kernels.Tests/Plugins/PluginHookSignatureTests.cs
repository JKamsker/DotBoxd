using System.Collections;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginHookSignatureTests
{
    [Fact]
    public async Task UseKernel_rejects_adapter_parameter_name_mismatch()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On(new MismatchedDamageEventAdapter()).UseKernel<FireDamageKernel>());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK033");
    }

    [Fact]
    public void UseGeneratedChain_rolls_back_when_adapter_signature_fails()
    {
        var server = PluginAddendumTestPolicies.CreateServer();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On(new MismatchedDamageEventAdapter())
                .UseGeneratedChain(FireDamagePluginPackage.Create()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK033");
        Assert.False(server.Kernels.TryGet("fire-damage", out _));
    }

    [Fact]
    public async Task Convention_adapter_uses_generated_event_parameter_names()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(ConventionPackage());

        server.Hooks.On<ConventionDamageEvent>().UseKernel(kernel);
        await server.Hooks.PublishAsync(new ConventionDamageEvent("fire", 120, "player-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("matched", message.Message);
    }

    [Fact]
    public async Task Convention_adapter_uses_record_constructor_property_order()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(ConventionRecordPackage());

        server.Hooks.On<ConventionRecordEvent>().UseKernel(kernel);
        await server.Hooks.PublishAsync(new ConventionRecordEvent(150, "player-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("record matched", message.Message);
    }

    [Fact]
    public async Task Direct_handle_rejects_unsubscribed_adapter_before_execution()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.HandleAsync(
                new AdminDamageEventAdapter(),
                new DamageEvent("fire", 120, "player-1")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK031");
        Assert.Empty(messages.Messages);
        Assert.Empty((IEnumerable)kernel.ExecutionObservations);
    }

    [Fact]
    public async Task Direct_should_handle_rejects_adapter_signature_mismatch_before_execution()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.ShouldHandleAsync(
                new MismatchedDamageEventAdapter(),
                new DamageEvent("fire", 120, "player-1")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK033");
        Assert.Empty((IEnumerable)kernel.ExecutionObservations);
    }

    [Fact]
    public async Task Install_supports_explicit_interface_event_adapter()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        server.RegisterEventAdapter(new ExplicitDamageEventAdapter());

        await server.InstallAsync(FireDamagePluginPackage.Create());
    }

    [Fact]
    public void On_rejects_different_adapter_after_pipeline_exists()
    {
        var server = DotBoxD.Plugins.PluginServer.Create();
        _ = server.Hooks.On(DamageEventAdapter.Instance);

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On(new MismatchedDamageEventAdapter()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK034");
    }

    private sealed class AdminDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "AdminEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }

    private sealed class MismatchedDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("wrongDamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
            SandboxValue.FromInt32(e.Amount),
            SandboxValue.FromString(e.TargetId)
            ];
    }

    private sealed class ExplicitDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        string IPluginEventAdapter<DamageEvent>.EventName => "DamageEvent";

        IReadOnlyList<Parameter> IPluginEventAdapter<DamageEvent>.Parameters => [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        IReadOnlyList<SandboxValue> IPluginEventAdapter<DamageEvent>.ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }

    private sealed record ConventionRecordEvent(int Value, string TargetId);

    private sealed record ConventionDamageEvent(string DamageType, int Amount, string TargetId);

    private static PluginPackage ConventionRecordPackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] {
            new("e_Value", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        };

        return PluginPackage.Create(
            new PluginManifest(
                "convention-record-adapter",
                "IEventKernel<ConventionRecordEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [],
                [new HookSubscriptionManifest(nameof(ConventionRecordEvent), "ConventionRecordKernel")])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
            new SandboxModule(
                "convention-record-adapter",
                SemVersion.One,
                SemVersion.One,
                [new CapabilityRequest(PluginMessageBindings.CapabilityId, "test notification")],
                [
                    new SandboxFunction(
                        "ShouldHandle",
                        true,
                        parameters,
                        SandboxType.Bool,
                        [
                            new ReturnStatement(
                                new BinaryExpression(
                                    new VariableExpression("e_Value", span),
                                    ">",
                                    new LiteralExpression(SandboxValue.FromInt32(100), span),
                                    span),
                                span)
                        ]),
                    new SandboxFunction(
                        "Handle",
                        true,
                        parameters,
                        SandboxType.Unit,
                        [
                            new ReturnStatement(
                                new CallExpression(
                                    PluginMessageBindings.SendBindingId,
                                    [
                                        new VariableExpression("e_TargetId", span),
                                        new LiteralExpression(SandboxValue.FromString("record matched"), span)
                                    ],
                                    null,
                                    span),
                                span)
                        ])
                ],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "convention-record-adapter",
                    ["kernel"] = "ConventionRecordKernel"
                }));
    }

    private static PluginPackage ConventionPackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] {
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        };

        return PluginPackage.Create(
            new PluginManifest(
                "convention-adapter",
                "IEventKernel<ConventionDamageEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [],
                [new HookSubscriptionManifest(nameof(ConventionDamageEvent), "ConventionKernel")])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
            new SandboxModule(
                "convention-adapter",
                SemVersion.One,
                SemVersion.One,
                [new CapabilityRequest(PluginMessageBindings.CapabilityId, "test notification")],
                [
                    new SandboxFunction(
                        "ShouldHandle",
                        true,
                        parameters,
                        SandboxType.Bool,
                        [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), span), span)]),
                    new SandboxFunction(
                        "Handle",
                        true,
                        parameters,
                        SandboxType.Unit,
                        [
                            new ReturnStatement(
                                new CallExpression(
                                    PluginMessageBindings.SendBindingId,
                                    [
                                        new VariableExpression("e_TargetId", span),
                                        new LiteralExpression(SandboxValue.FromString("matched"), span)
                                    ],
                                    null,
                                    span),
                                span)
                        ])
                ],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "convention-adapter",
                    ["kernel"] = "ConventionKernel"
                }));
    }
}
