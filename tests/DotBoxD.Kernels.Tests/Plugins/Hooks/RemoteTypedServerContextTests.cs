using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteTypedServerContextTests
{
    private sealed record RemoteEvent(string Id);

    [Hook("remote.result", typeof(RemoteResult))]
    private sealed record RemoteResultEvent(string Id);

    private sealed record RemoteContext(HookContext Raw, string Source);

    private readonly record struct RemoteResult(bool Success, string? Reason, int Length) : IHookResult;

    [Fact]
    public async Task Remote_hook_RunLocal_invokes_the_configured_context_type()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        string? subscriptionId = null;
        var registry = new RemoteHookRegistry(package =>
        {
            subscriptionId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
            return ValueTask.FromResult(subscriptionId);
        }, localHandlers);
        string? observed = null;

        registry.On<RemoteEvent, RemoteContext>(ctx => new RemoteContext(ctx, "hook"))
            .UseGeneratedLocalChain(
                PackageFor<RemoteEvent>(projectedType: "global::" + typeof(RemoteEvent).FullName),
                (RemoteEvent e, RemoteContext ctx) =>
                {
                    observed = ctx.Source + ":" + e.Id;
                    return ValueTask.CompletedTask;
                });

        await localHandlers.DispatchAsync(subscriptionId!, Encode(new RemoteEvent("evt")), RawContext());

        Assert.Equal("hook:evt", observed);
    }

    [Fact]
    public async Task Remote_subscription_RunLocal_stage_invokes_the_configured_context_type()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        string? subscriptionId = null;
        var registry = new RemoteSubscriptionRegistry(package =>
        {
            subscriptionId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
            return ValueTask.FromResult(subscriptionId);
        }, localHandlers);
        string? observed = null;

        registry.On<RemoteEvent, RemoteContext>(ctx => new RemoteContext(ctx, "subscription"))
            .Select(e => e.Id)
            .UseGeneratedLocalChain(
                PackageFor<RemoteEvent>(projectedType: "string"),
                (string id, RemoteContext ctx) =>
                {
                    observed = ctx.Source + ":" + id;
                    return ValueTask.CompletedTask;
                });

        await localHandlers.DispatchAsync(subscriptionId!, Encode("projected"), RawContext());

        Assert.Equal("subscription:projected", observed);
    }

    [Fact]
    public async Task Remote_hook_RegisterLocal_invokes_the_configured_context_type()
    {
        var localHandlers = new RemoteLocalHandlerRegistry();
        string? subscriptionId = null;
        var registry = new RemoteHookRegistry(package =>
        {
            subscriptionId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
            return ValueTask.FromResult(subscriptionId);
        }, localHandlers);
        string? observed = null;

        registry.On<RemoteResultEvent, RemoteContext>(ctx => new RemoteContext(ctx, "result"))
            .UseGeneratedLocalResultChain<RemoteResult>(
                ResultPackage(),
                (e, ctx) =>
                {
                    observed = ctx.Source + ":" + e.Id;
                    return new RemoteResult(true, ctx.Source, e.Id.Length);
                });

        var response = await localHandlers.DispatchResultAsync(
            subscriptionId!,
            Encode(new RemoteResultEvent("abc")),
            RawContext());

        Assert.Equal("result:abc", observed);
        var result = KernelRpcBinaryCodec.DecodeValue(response);
        Assert.True(result.Items[0].BoolValue);
        Assert.Equal("result", result.Items[1].TextValue);
        Assert.Equal(3, result.Items[2].Int32Value);
    }

    private static HookContext RawContext()
        => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private static byte[] Encode<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private static PluginPackage PackageFor<TEvent>(string? projectedType)
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    new HookSubscriptionManifest(typeof(TEvent).FullName!, "FireDamageKernel")
                    {
                        LocalTerminal = true,
                        ProjectedType = projectedType
                    }
                ]
            }
        };
    }

    private static PluginPackage ResultPackage()
    {
        var package = FireDamagePluginPackage.Create();
        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    new HookSubscriptionManifest(typeof(RemoteResultEvent).FullName!, "FireDamageKernel")
                    {
                        ResultType = typeof(RemoteResult).FullName,
                        ResultLocalTerminal = true
                    }
                ]
            }
        };
    }
}
