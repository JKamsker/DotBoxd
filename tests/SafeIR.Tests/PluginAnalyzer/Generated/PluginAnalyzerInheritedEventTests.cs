using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerInheritedEventTests
{
    [Fact]
    public async Task Generated_package_matches_convention_adapter_for_inherited_event_properties()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;

            namespace Sample;

            public abstract class InheritedDamageEventBase
            {
                protected InheritedDamageEventBase(string targetId)
                {
                    TargetId = targetId;
                }

                public string TargetId { get; }
            }

            public sealed class InheritedDamageEvent : InheritedDamageEventBase
            {
                public InheritedDamageEvent(string targetId, string message)
                    : base(targetId)
                {
                    Message = message;
                }

                public string Message { get; }
            }

            [GamePlugin("generated-inherited-event")]
            public sealed partial class DamageKernel : IEventKernel<InheritedDamageEvent>
            {
                public bool ShouldHandle(InheritedDamageEvent e, HookContext ctx)
                    => e.TargetId == "player-1";

                public void Handle(InheritedDamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);

        server.Hooks.On<InheritedDamageEvent>().UseKernel(kernel);
        await server.Hooks.PublishAsync(new InheritedDamageEvent("other", "ignored"));
        await server.Hooks.PublishAsync(new InheritedDamageEvent("player-1", "inherited matched"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("inherited matched", message.Message);
    }

    private abstract class InheritedDamageEventBase
    {
        protected InheritedDamageEventBase(string targetId)
        {
            TargetId = targetId;
        }

        public string TargetId { get; }
    }

    private sealed class InheritedDamageEvent : InheritedDamageEventBase
    {
        public InheritedDamageEvent(string targetId, string message)
            : base(targetId)
        {
            Message = message;
        }

        public string Message { get; }
    }
}
