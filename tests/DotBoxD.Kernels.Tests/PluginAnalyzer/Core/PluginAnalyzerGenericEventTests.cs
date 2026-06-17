using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerGenericEventTests
{
    [Fact]
    public async Task Generated_package_matches_convention_adapter_for_generic_event_type()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record GenericDamageEvent<T>(T Payload, string TargetId, string Message);

            [Plugin("generated-generic-event")]
            public sealed partial class GenericDamageKernel : IEventKernel<GenericDamageEvent<string>>
            {
                public bool ShouldHandle(GenericDamageEvent<string> e, HookContext ctx)
                    => e.Payload == "fire";

                public void Handle(GenericDamageEvent<string> e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "Sample.GenericDamagePluginPackage");
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);

        server.Hooks.On<GenericDamageEvent<string>>().Use(kernel);
        await server.Hooks.PublishAsync(new GenericDamageEvent<string>("ice", "player-1", "ignored"));
        await server.Hooks.PublishAsync(new GenericDamageEvent<string>("fire", "player-1", "generic matched"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("generic matched", message.Message);
    }

    [Fact]
    public void Hook_registry_rejects_ambiguous_generic_event_adapter_shapes()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        _ = server.Hooks.On<GenericDamageEvent<int>>();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<GenericDamageEvent<string>>());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK034");
    }

    private sealed record GenericDamageEvent<T>(T Payload, string TargetId, string Message);
}
