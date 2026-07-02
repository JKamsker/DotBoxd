using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private sealed record SameShapeAggroProjection(string MonsterId, int Distance);

    [Fact]
    public void Remote_RunLocal_rejects_direct_local_terminal_flag_tamper()
    {
        var package = WithSubscription(
            LowerToPackage(RemoteRunLocalSource),
            subscription => subscription with { LocalTerminal = false });

        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<ChainAggroEvent>()
                .Select(e => e.MonsterId)
                .UseGeneratedLocalChain(package, (string _, HookContext _) => ValueTask.CompletedTask));

        Assert.False(installed);
        Assert.Contains("localTerminal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RunLocal_rejects_json_projected_type_tamper()
    {
        var package = JsonRoundTrip(WithSubscription(
            LowerToPackage(RemoteRunLocalSource),
            subscription => subscription with { ProjectedType = "int" }));

        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<ChainAggroEvent>()
                .Select(e => e.MonsterId)
                .UseGeneratedLocalChain(package, (string _, HookContext _) => ValueTask.CompletedTask));

        Assert.False(installed);
        Assert.Contains("projectedType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RunLocal_rejects_list_projection_handler_element_type_mismatch_before_install()
    {
        var package = LowerToPackage(ListProjectionSource);
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<ScoreEvent>()
                .Select(_ => new List<string>())
                .UseGeneratedLocalChain(package, (List<string> _, HookContext _) => ValueTask.CompletedTask));

        Assert.False(installed);
        Assert.Contains("projectedType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_whole_event_RunLocal_rejects_direct_projected_type_tamper()
    {
        var package = WithSubscription(
            LowerToPackage(RemoteWholeEventSource),
            subscription => subscription with { ProjectedType = "string" });

        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<ChainAggroEvent>()
                .UseGeneratedLocalChain(package, (ChainAggroEvent _, HookContext _) => ValueTask.CompletedTask));

        Assert.False(installed);
        Assert.Contains("projectedType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_whole_event_RunLocal_rejects_same_shape_wrong_record_handler_before_install()
    {
        var package = LowerToPackage(RemoteWholeEventSource);
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<ChainAggroEvent>()
                .Select(e => new SameShapeAggroProjection(e.MonsterId, e.Distance))
                .UseGeneratedLocalChain(
                    package,
                    (SameShapeAggroProjection _, HookContext _) => ValueTask.CompletedTask));

        Assert.False(installed);
        Assert.Contains("projectedType", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SameShapeAggroProjection).FullName!, exception.Message, StringComparison.Ordinal);
    }

    private static PluginPackage JsonRoundTrip(PluginPackage package)
        => PluginPackageJsonSerializer.Import(PluginPackageJsonSerializer.Export(package));

    private static PluginPackage WithSubscription(
        PluginPackage package,
        Func<HookSubscriptionManifest, HookSubscriptionManifest> mutate)
    {
        var subscriptions = package.Manifest.Subscriptions.Select(mutate).ToArray();
        return package with
        {
            Manifest = package.Manifest with { Subscriptions = subscriptions }
        };
    }
}
