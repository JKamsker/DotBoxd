using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteSubscriptionStageTests
{
    [Fact]
    public void UseGeneratedLocalChain_validates_package_before_delegating()
    {
        var stage = Stage();

        Assert.Throws<ArgumentNullException>(
            () => stage.UseGeneratedLocalChain(null!, (_, _) => ValueTask.CompletedTask));
    }

    [Fact]
    public void UseGeneratedLocalChain_validates_handler_before_delegating()
    {
        var stage = Stage();

        Assert.Throws<ArgumentNullException>(
            () => stage.UseGeneratedLocalChain(Package(), (Func<string, HookContext, ValueTask>)null!));
    }

    [Fact]
    public void UseGeneratedChain_accepts_declared_hook_name_for_ordinary_subscription_packages()
    {
        var installed = false;
        var registry = new RemoteSubscriptionRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("installed");
        });

        registry.On<HookNamedStageEvent>().UseGeneratedChain(HookNamedPackage());

        Assert.True(installed);
    }

    private static RemoteSubscriptionStage<StageEvent, string> Stage()
        => new RemoteSubscriptionRegistry(_ => throw new InvalidOperationException(), new RemoteLocalHandlerRegistry())
            .On<StageEvent>()
            .Select(e => e.Id);

    private static PluginPackage Package()
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest(typeof(StageEvent).FullName!, "FireDamageKernel")]
            }
        };
    }

    private static PluginPackage HookNamedPackage()
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest("stage.hook", "FireDamageKernel")]
            }
        };
    }

    private sealed record StageEvent(string Id);

    [Hook("stage.hook", typeof(StageHookResult))]
    private sealed record HookNamedStageEvent(string Id);

    private readonly record struct StageHookResult(bool Success, string? Reason) : IHookResult;
}
