using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class PluginRevocationTests
{
    [Fact]
    public async Task Uninstall_revokes_existing_hook_pipeline_kernel_reference()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        var removed = server.Uninstall("fire-damage");
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-2"));

        Assert.True(removed);
        Assert.True(kernel.IsRevoked);
        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
    }

    [Fact]
    public async Task Reinstall_revokes_previous_kernel_captured_by_hook_pipeline()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        var first = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        var replacement = await server.InstallAsync(FireDamagePluginPackage.Create());
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-2"));
        server.Hooks.On<DamageEvent>().UseKernel(replacement);
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-3"));
        var removed = server.Uninstall("fire-damage");
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-4"));

        Assert.True(first.IsRevoked);
        Assert.True(removed);
        Assert.True(replacement.IsRevoked);
        Assert.Equal(["player-1", "player-3"], messages.Messages.Select(m => m.TargetId));
    }

    [Fact]
    public async Task Revoked_kernel_handle_rejects_direct_execution()
    {
        var server = PluginServer.Create();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Uninstall("fire-damage"));

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await kernel.HandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1")).AsTask());

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
    }

    [Fact]
    public async Task Uninstall_between_filter_and_handler_skips_handler()
    {
        var messages = new InMemoryPluginMessageSink();
        var blocking = new BlockingShouldHandleBinding();
        var server = PluginServer.Create(
            messages,
            builder => builder.AddBinding(blocking.Descriptor()),
            LongWallPluginPolicy());
        var kernel = await server.InstallAsync(BlockingPackage());
        server.Hooks.On(BlockingEventAdapter.Instance).UseKernel(kernel);

        var publish = server.Hooks.PublishAsync(new BlockingEvent("player-1")).AsTask();
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(server.Uninstall("revocation-blocking"));
        blocking.Release.SetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(messages.Messages);
    }

    private sealed class BlockingShouldHandleBinding
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BindingDescriptor Descriptor()
            => new(
                "test.blockShouldHandle",
                SemVersion.One,
                [],
                SandboxType.Bool,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                async (_, _, cancellationToken) =>
                {
                    Started.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return SandboxValue.FromBool(true);
                },
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
    }

    private sealed record BlockingEvent(string TargetId);

    private sealed class BlockingEventAdapter : IPluginEventAdapter<BlockingEvent>
    {
        public static BlockingEventAdapter Instance { get; } = new();
        public string EventName => "blocking.event";
        public IReadOnlyList<Parameter> Parameters => [new("targetId", SandboxType.String)];
        public IReadOnlyList<SandboxValue> ToSandboxValues(BlockingEvent e) => [SandboxValue.FromString(e.TargetId)];
    }

    private static PluginPackage BlockingPackage()
    {
        var span = new SourceSpan(1, 1);
        return PluginPackage.Create(
            new PluginManifest(
                "revocation-blocking",
                "IEventKernel<BlockingEvent>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc", "GameStateWrite", "Audit"],
                [],
                [new HookSubscriptionManifest("blocking.event", "BlockingKernel")]),
            new SandboxModule(
                "revocation-blocking",
                SemVersion.One,
                SemVersion.One,
                [new CapabilityRequest(PluginMessageBindings.CapabilityId, "test notification")],
                [BlockingShouldHandle(span), BlockingHandle(span)],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "revocation-blocking",
                    ["kernel"] = "BlockingKernel"
                }));
    }

    private static SandboxFunction BlockingShouldHandle(SourceSpan span)
        => new(
            "ShouldHandle",
            true,
            [new Parameter("targetId", SandboxType.String)],
            SandboxType.Bool,
            [new ReturnStatement(new CallExpression("test.blockShouldHandle", [], null, span), span)]);

    private static SandboxFunction BlockingHandle(SourceSpan span)
        => new(
            "Handle",
            true,
            [new Parameter("targetId", SandboxType.String)],
            SandboxType.Unit,
            [new ReturnStatement(
                new CallExpression(
                    PluginMessageBindings.SendBindingId,
                    [
                        new VariableExpression("targetId", span),
                        new LiteralExpression(SandboxValue.FromString("message"), span)
                    ],
                    null,
                    span),
                span)]);

    private static SandboxPolicy LongWallPluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantGameMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
