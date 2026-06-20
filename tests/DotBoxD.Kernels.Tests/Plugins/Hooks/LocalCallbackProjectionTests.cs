using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class LocalCallbackProjectionTests
{
    [Fact]
    public void IsWholeEvent_accepts_multiple_local_terminals_with_the_same_projected_type_state()
    {
        var manifest = Manifest(
            new HookSubscriptionManifest("IgnoredEvent", "IgnoredKernel"),
            new HookSubscriptionManifest("Event", "Kernel") { LocalTerminal = true, ProjectedType = "string" },
            new HookSubscriptionManifest("Event", "Kernel") { LocalTerminal = true, ProjectedType = "int" });

        Assert.False(LocalCallbackProjection.IsWholeEvent(manifest));
    }

    [Fact]
    public void IsWholeEvent_rejects_conflicting_local_terminal_projected_type_states()
    {
        var manifest = Manifest(
            new HookSubscriptionManifest("Event", "Kernel") { LocalTerminal = true },
            new HookSubscriptionManifest("Event", "Kernel") { LocalTerminal = true, ProjectedType = "string" });

        var ex = Assert.Throws<InvalidOperationException>(() => LocalCallbackProjection.IsWholeEvent(manifest));

        Assert.Contains("conflicting local-terminal projected type", ex.Message, StringComparison.Ordinal);
    }

    private static PluginManifest Manifest(params HookSubscriptionManifest[] subscriptions)
        => new(
            "local-projection-test",
            "IEventKernel<Event>",
            ExecutionMode.Interpreted,
            [],
            [],
            subscriptions);
}
