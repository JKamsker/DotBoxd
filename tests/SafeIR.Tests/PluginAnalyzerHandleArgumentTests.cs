using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerHandleArgumentTests
{
    [Fact]
    public async Task Generated_handle_respects_reversed_send_named_arguments()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record NamedSendEvent(string TargetId, string Message);

            [GamePlugin("generated-named-send")]
            public sealed partial class NamedSendKernel : IEventKernel<NamedSendEvent>
            {
                public bool ShouldHandle(NamedSendEvent e, HookContext ctx) => true;

                public void Handle(NamedSendEvent e, HookContext ctx)
                    => ctx.Messages.Send(message: e.Message, targetId: e.TargetId);
            }
            """, "Sample.NamedSendPluginPackage");
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        var kernel = await server.InstallAsync(package);

        await kernel.HandleAsync(new NamedSendEventAdapter(), new NamedSendEvent("player-1", "named message"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("named message", message.Message);
    }

    [Fact]
    public async Task Generated_handle_allows_explicit_return_after_send()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;

            namespace Sample;

            public sealed record NamedSendEvent(string TargetId, string Message);

            [GamePlugin("generated-return-after-send")]
            public sealed partial class NamedSendKernel : IEventKernel<NamedSendEvent>
            {
                public bool ShouldHandle(NamedSendEvent e, HookContext ctx) => true;

                public void Handle(NamedSendEvent e, HookContext ctx)
                {
                    ctx.Messages.Send(e.TargetId, e.Message);
                    return;
                }
            }
            """, "Sample.NamedSendPluginPackage");
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        var kernel = await server.InstallAsync(package);

        await kernel.HandleAsync(new NamedSendEventAdapter(), new NamedSendEvent("player-1", "returned"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("returned", message.Message);
    }

    private sealed record NamedSendEvent(string TargetId, string Message);

    private sealed class NamedSendEventAdapter : IPluginEventAdapter<NamedSendEvent>
    {
        public string EventName => nameof(NamedSendEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(NamedSendEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message)
            ];
    }
}
