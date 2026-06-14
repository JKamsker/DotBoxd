using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginInputAllocationTests
{
    [Fact]
    public async Task Kernel_input_building_does_not_enumerate_event_value_list_when_live_settings_exist()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(InputBuildPackage());

        await kernel.HandleAsync(new IndexOnlyEventAdapter(), new IndexOnlyEvent("player-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("matched", message.Message);
    }

    private static PluginPackage InputBuildPackage()
    {
        var span = new SourceSpan(1, 1);
        var parameters = new Parameter[] {
            new("e_TargetId", SandboxType.String),
            new("Enabled", SandboxType.Bool)
        };

        return PluginPackage.Create(
            new PluginManifest(
                "input-build",
                "IEventKernel<IndexOnlyEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "HostStateWrite", "Audit"],
                [new LiveSettingDefinition("Enabled", "bool", true)],
                [new HookSubscriptionManifest(nameof(IndexOnlyEvent), "InputBuildKernel")]),
            new SandboxModule(
                "input-build",
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
                    ["pluginId"] = "input-build",
                    ["kernel"] = "InputBuildKernel"
                }));
    }

    private sealed record IndexOnlyEvent(string TargetId);

    private sealed class IndexOnlyEventAdapter : IPluginEventAdapter<IndexOnlyEvent>
    {
        public string EventName => nameof(IndexOnlyEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(IndexOnlyEvent e)
            => new IndexOnlyValues(SandboxValue.FromString(e.TargetId));
    }

    private sealed class IndexOnlyValues(SandboxValue value) : IReadOnlyList<SandboxValue>
    {
        public int Count => 1;

        public SandboxValue this[int index]
            => index == 0 ? value : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<SandboxValue> GetEnumerator()
            => throw new InvalidOperationException("Kernel input building should copy event values by index.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
