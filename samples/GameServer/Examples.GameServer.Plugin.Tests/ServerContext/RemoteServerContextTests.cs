using DotBoxD.Abstractions;
using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed class RemoteServerContextTests
{
    [Fact]
    public async Task RunLocal_can_consume_context_defined_by_the_example_server_contract()
    {
        var observed = new List<(string MonsterId, bool TokenCanBeCanceled)>();
        var localHandlers = new RemoteLocalHandlerRegistry();
        PluginPackage? lowered = null;
        string? subscriptionId = null;
        var hooks = new GamePluginHookRegistry(
            package =>
            {
                lowered = package;
                subscriptionId = package.Manifest.PluginId;
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            localHandlers);

        LocalReactions.ConfigureServerContextReaction(
            hooks,
            (monsterId, tokenCanBeCanceled) => observed.Add((monsterId, tokenCanBeCanceled)));

        Assert.NotNull(lowered);
        Assert.NotNull(subscriptionId);
        var subscription = Assert.Single(lowered!.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);

        using var cts = new CancellationTokenSource();
        await localHandlers.DispatchAsync(
            subscriptionId!,
            Encode("monster-ctx"),
            new HookContext(new InMemoryPluginMessageSink(), cts.Token),
            cts.Token);

        Assert.Equal([("ctx:monster-ctx", true)], observed);
    }

    private static byte[] Encode<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }
}
