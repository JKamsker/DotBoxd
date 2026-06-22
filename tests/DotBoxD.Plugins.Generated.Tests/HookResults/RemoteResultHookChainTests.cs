using DotBoxD.Abstractions;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

[Hook("remote.damage", typeof(RemoteDamageResult))]
public sealed record RemoteDamageContext(int Damage);

[HookResult]
public readonly partial record struct RemoteDamageResult(bool Success, string? Reason, int Damage);

[Hook("remote.damage", typeof(RemoteOtherDamageResult))]
public sealed record RemoteOtherDamageContext(int Damage);

[HookResult]
public readonly partial record struct RemoteOtherDamageResult(bool Success, string? Reason, int Damage);

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
    public void Remote_result_install_rejects_matching_hook_name_with_wrong_result_type()
    {
        var package = CaptureSandboxPackage();
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteOtherDamageContext>().UseGeneratedResultChain<RemoteOtherDamageResult>(package));

        Assert.False(installed);
        Assert.Contains("remote.damage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_result_install_rejects_full_event_name_match_with_wrong_result_type()
    {
        var package = WithSubscription(
            CaptureSandboxPackage(),
            subscription => subscription with
            {
                Event = typeof(RemoteDamageContext).FullName!,
                ResultType = "global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteOtherDamageResult"
            });
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedResultChain<RemoteDamageResult>(package));

        Assert.False(installed);
        Assert.Contains("RemoteOtherDamageResult", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_Register_rejects_generic_result_type_that_does_not_match_manifest()
    {
        var package = CaptureSandboxPackage();
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedResultChain<RemoteOtherDamageResult>(package));

        Assert.False(installed);
        Assert.Contains(nameof(RemoteOtherDamageResult), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RegisterLocal_rejects_non_local_result_manifest_before_registering_callback()
    {
        var package = WithSubscription(
            CaptureLocalPackage(),
            subscription => subscription with { ResultLocalTerminal = false });
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
                package,
                (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage)));

        Assert.False(installed);
        Assert.Contains("resultLocalTerminal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RegisterLocal_rejects_handler_result_type_that_does_not_match_manifest()
    {
        var package = CaptureLocalPackage();
        var installed = false;
        var registry = new RemoteHookRegistry(_ =>
        {
            installed = true;
            return ValueTask.FromResult("unused");
        }, new RemoteLocalHandlerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
                package,
                (RemoteDamageContext _, HookContext _) => RemoteOtherDamageResult.Ok().WithDamage(1)));

        Assert.False(installed);
        Assert.Contains(nameof(RemoteOtherDamageResult), exception.Message, StringComparison.Ordinal);
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
            TimeSpan.FromMilliseconds(100),
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

    private static PluginPackage CaptureSandboxPackage()
    {
        PluginPackage? captured = null;
        var registry = new RemoteHookRegistry(package =>
        {
            captured = package;
            return ValueTask.FromResult(package.Manifest.PluginId);
        });

        RemoteDamagePlugin.ConfigureSandbox(registry);
        return captured ?? throw new InvalidOperationException("Remote damage package was not captured.");
    }

    private static PluginPackage CaptureLocalPackage()
    {
        PluginPackage? captured = null;
        var registry = new RemoteHookRegistry(
            package =>
            {
                captured = package;
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            new RemoteLocalHandlerRegistry());

        RemoteDamagePlugin.ConfigureLocal(registry);
        return captured ?? throw new InvalidOperationException("Remote local damage package was not captured.");
    }

    private static PluginPackage WithSubscription(
        PluginPackage package,
        Func<HookSubscriptionManifest, HookSubscriptionManifest> transform)
        => package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [transform(Assert.Single(package.Manifest.Subscriptions))]
            }
        };
}
