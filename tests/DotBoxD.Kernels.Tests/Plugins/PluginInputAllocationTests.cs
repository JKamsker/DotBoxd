using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins;

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

    [Fact]
    public async Task Kernel_input_building_prefers_event_value_writer_when_available()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(InputBuildPackage());

        await kernel.HandleAsync(new WriterEventAdapter(), new IndexOnlyEvent("player-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("matched", message.Message);
    }

    [Fact]
    public async Task Kernel_input_building_rejects_writer_value_count_mismatch()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(InputBuildPackage());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.HandleAsync(new MismatchedWriterEventAdapter(), new IndexOnlyEvent("player-1")).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK036");
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public void Register_event_adapter_rejects_writer_value_count_mismatch()
    {
        var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.RegisterEventAdapter(new MismatchedWriterEventAdapter()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK036");
    }

    [Fact]
    public void Live_state_sync_without_synchronizers_reuses_empty_deferred_update_list()
    {
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.Sync);

        var first = registry.SynchronizeForInput();
        var second = registry.SynchronizeForInput();

        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Live_state_sync_with_sync_updates_reuses_empty_deferred_update_list()
    {
        var calls = 0;
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.Sync);
        registry.Register(typeof(IndexOnlyEvent), () => calls++);

        var first = registry.SynchronizeForInput();
        var second = registry.SynchronizeForInput();

        Assert.Equal(2, calls);
        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Live_state_sync_with_async_updates_returns_deferred_actions()
    {
        var calls = 0;
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.AsyncSet);
        registry.Register(typeof(IndexOnlyEvent), () => calls++);

        var deferredUpdates = registry.SynchronizeForInput();

        var update = Assert.Single(deferredUpdates);
        Assert.Equal(0, calls);
        update();
        Assert.Equal(1, calls);
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
                ["Cpu", "Alloc", "HostStateWrite", "Concurrency", "Audit"],
                [new LiveSettingDefinition("Enabled", "bool", true)],
                [new HookSubscriptionManifest(nameof(IndexOnlyEvent), "InputBuildKernel")])
            {
                RequiredCapabilities = [RuntimeCapabilityIds.Async, PluginMessageBindings.CapabilityId]
            },
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

    private sealed class WriterEventAdapter : IPluginEventValueWriter<IndexOnlyEvent>
    {
        public string EventName => nameof(IndexOnlyEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public int EventValueCount => 1;

        public IReadOnlyList<SandboxValue> ToSandboxValues(IndexOnlyEvent e)
            => throw new InvalidOperationException("Writer adapters should not allocate event value lists.");

        public SandboxValue ToSandboxValue(IndexOnlyEvent e, int index)
            => index == 0 ? SandboxValue.FromString(e.TargetId) : throw new ArgumentOutOfRangeException(nameof(index));

        public void CopySandboxValues(IndexOnlyEvent e, SandboxValue[] destination, int destinationIndex)
            => destination[destinationIndex] = SandboxValue.FromString(e.TargetId);
    }

    private sealed class MismatchedWriterEventAdapter : IPluginEventValueWriter<IndexOnlyEvent>
    {
        public string EventName => nameof(IndexOnlyEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public int EventValueCount => 2;

        public IReadOnlyList<SandboxValue> ToSandboxValues(IndexOnlyEvent e)
            => throw new InvalidOperationException("Writer adapters should not allocate event value lists.");

        public SandboxValue ToSandboxValue(IndexOnlyEvent e, int index)
            => index == 0 ? SandboxValue.FromString(e.TargetId) : throw new ArgumentOutOfRangeException(nameof(index));

        public void CopySandboxValues(IndexOnlyEvent e, SandboxValue[] destination, int destinationIndex)
            => destination[destinationIndex] = SandboxValue.FromString(e.TargetId);
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
