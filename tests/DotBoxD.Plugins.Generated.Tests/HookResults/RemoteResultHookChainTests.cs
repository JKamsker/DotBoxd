using DotBoxD.Abstractions;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

[Hook("remote.damage", typeof(RemoteDamageResult))]
public sealed record RemoteDamageContext(int Damage);

[HookResult]
public readonly partial record struct RemoteDamageResult(bool Success, string? Reason, int Damage);

public static class RemoteDamagePlugin
{
    public static void ConfigureLocal(RemoteHookRegistry hooks)
        => hooks.On<RemoteDamageContext>()
            .Where(ctx => ctx.Damage > 10)
            .RegisterLocal((ctx, hookContext) => RemoteDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 7);

    public static void ConfigureSandbox(RemoteHookRegistry hooks)
        => hooks.On<RemoteDamageContext>()
            .Where(ctx => ctx.Damage > 10)
            .Register(ctx => RemoteDamageResult.Ok().WithDamage(ctx.Damage * 3), priority: 11);
}

public sealed class RemoteResultHookChainTests
{
    [Fact]
    public async Task Remote_RegisterLocal_filters_server_side_and_requests_result_from_plugin_side_delegate()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(server, localHandlers);

        RemoteDamagePlugin.ConfigureLocal(registry);

        var miss = await server.Hooks.FireAsync(new RemoteDamageContext(5));
        Assert.Null(miss);

        var hit = await server.Hooks.FireAsync(new RemoteDamageContext(12));
        Assert.True(hit!.Value.Success);
        Assert.Equal(24, hit.Value.Damage);
    }

    [Fact]
    public async Task Remote_Register_installs_sandbox_result_handler_on_the_server()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(server, localHandlers);

        RemoteDamagePlugin.ConfigureSandbox(registry);

        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.True(result!.Value.Success);
        Assert.Equal(36, result.Value.Damage);
    }

    [Fact]
    public async Task Remote_RegisterLocal_timeout_options_return_fail_closed_result_when_request_hangs()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(
            defaultPolicy: TestPolicies.Chain(),
            onResultHookFault: faults.Add);
        var localHandlers = new RemoteLocalHandlerRegistry();
        var never = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = RemoteRegistry(server, localHandlers, (_, _, _) => new ValueTask<byte[]>(never.Task));

        RemoteDamagePlugin.ConfigureLocal(registry);

        var options = ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
            TimeSpan.FromMilliseconds(25),
            new RemoteDamageResult(true, "timeout", -1));
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12), options);

        Assert.Equal(-1, result!.Value.Damage);
        Assert.IsType<TimeoutException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public async Task Remote_RegisterLocal_result_request_receives_the_fire_token()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        CancellationToken observed = default;
        var registry = RemoteRegistry(server, localHandlers, (id, payload, token) =>
        {
            observed = token;
            return localHandlers.DispatchResultAsync(
                id,
                payload.ToArray(),
                new HookContext(new InMemoryPluginMessageSink(), token),
                token);
        });
        using var cts = new CancellationTokenSource();

        RemoteDamagePlugin.ConfigureLocal(registry);
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12), cts.Token);

        Assert.Equal(cts.Token, observed);
        Assert.Equal(24, result!.Value.Damage);
    }

    private static RemoteHookRegistry RemoteRegistry(
        PluginServer server,
        RemoteLocalHandlerRegistry localHandlers,
        RemoteLocalResultRequest? requestOverride = null)
        => new(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscription = Assert.Single(package.Manifest.Subscriptions);
                var subscriptionId = package.Manifest.PluginId;
                var pipeline = server.Hooks.On<RemoteDamageContext>();
                if (subscription.ResultLocalTerminal)
                {
                    pipeline.UseProjectingResult(
                        kernel,
                        subscriptionId,
                        typeof(RemoteDamageResult),
                        requestOverride ?? ((id, payload, token) => localHandlers.DispatchResultAsync(
                                id,
                                payload.ToArray(),
                                new HookContext(new InMemoryPluginMessageSink(), token),
                                token)),
                        subscription.Priority);
                }
                else
                {
                    pipeline.UseResult(kernel, typeof(RemoteDamageResult), subscription.Priority);
                }

                return subscriptionId;
            },
            localHandlers);
}
