using DotBoxD.Abstractions;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public sealed partial class RemoteResultHookChainTests
{
    [Fact]
    public async Task Remote_RegisterLocal_uses_hook_point_timeout_defaults_for_inferred_fire_async()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(
            defaultPolicy: TestPolicies.Chain(),
            onResultHookFault: faults.Add);
        var localHandlers = new RemoteLocalHandlerRegistry();
        var never = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Hooks.On<RemoteDamageContext>().ConfigureResultDispatch(
            ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(100),
                new RemoteDamageResult(true, "timeout", -2)));
        var registry = RemoteRegistry(server, localHandlers, (_, _, _) => new ValueTask<byte[]>(never.Task));

        RemoteDamagePlugin.ConfigureLocal(registry);
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.Equal(-2, result!.Value.Damage);
        Assert.IsType<TimeoutException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public async Task Remote_RegisterLocal_remote_cancellation_is_fault_not_timeout()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(
            defaultPolicy: TestPolicies.Chain(),
            onResultHookFault: faults.Add);
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(
            server,
            localHandlers,
            (_, _, _) => throw new OperationCanceledException("remote transport canceled"));

        RemoteDamagePlugin.ConfigureLocal(registry);
        var options = ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
            TimeSpan.FromSeconds(30),
            new RemoteDamageResult(true, "timeout", -1));
        var result = await server.Hooks.FireAsync<RemoteDamageContext, RemoteDamageResult>(
            new RemoteDamageContext(12),
            options);

        Assert.Null(result);
        Assert.IsType<OperationCanceledException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public async Task Remote_RegisterLocal_encodes_nullable_result_fields()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = new RemoteHookRegistry(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscriptionId = kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;
                server.Hooks.On<RemoteOptionalContext>().UseProjectingResult(
                    kernel,
                    subscriptionId,
                    typeof(RemoteOptionalResult),
                    (id, payload, token) => localHandlers.DispatchResultAsync(
                        id,
                        payload.ToArray(),
                        new HookContext(new InMemoryPluginMessageSink(), token),
                        token),
                    Assert.Single(package.Manifest.Subscriptions).Priority);
                return subscriptionId;
            },
            localHandlers);

        RemoteOptionalPlugin.ConfigureLocal(registry);

        var missing = await server.Hooks.FireAsync(new RemoteOptionalContext(0));
        var present = await server.Hooks.FireAsync(new RemoteOptionalContext(7));

        Assert.Null(missing!.Value.Damage);
        Assert.Equal(7, present!.Value.Damage);
    }

    [Fact]
    public async Task Remote_result_hook_json_round_trip_preserves_sandbox_metadata()
    {
        var imported = PluginPackageJsonSerializer.Import(
            PluginPackageJsonSerializer.Export(CaptureSandboxPackage()));
        var subscription = Assert.Single(imported.Manifest.Subscriptions);

        Assert.Equal("global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteDamageResult", subscription.ResultType);
        Assert.False(subscription.ResultLocalTerminal);
        Assert.Equal(11, subscription.Priority);

        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var kernel = await server.InstallAsync(imported);
        server.Hooks.On<RemoteDamageContext>().UseResult(
            kernel,
            typeof(RemoteDamageResult),
            subscription.Priority);

        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.Equal(36, result!.Value.Damage);
    }

    [Fact]
    public async Task Remote_result_hook_json_round_trip_preserves_local_metadata()
    {
        var imported = PluginPackageJsonSerializer.Import(
            PluginPackageJsonSerializer.Export(CaptureLocalPackage()));
        var subscription = Assert.Single(imported.Manifest.Subscriptions);
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(server, localHandlers);

        Assert.Equal("global::DotBoxD.Plugins.Generated.Tests.HookResults.RemoteDamageResult", subscription.ResultType);
        Assert.True(subscription.ResultLocalTerminal);
        Assert.Equal(7, subscription.Priority);

        registry.On<RemoteDamageContext>().UseGeneratedLocalResultChain(
            imported,
            (RemoteDamageContext context, HookContext _) => RemoteDamageResult.Ok().WithDamage(context.Damage * 2));
        var result = await server.Hooks.FireAsync(new RemoteDamageContext(12));

        Assert.Equal(24, result!.Value.Damage);
    }

    [Fact]
    public async Task Remote_RegisterLocal_result_request_receives_timeout_token_by_default()
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

        Assert.True(observed.CanBeCanceled);
        Assert.NotEqual(cts.Token, observed);
        Assert.Equal(24, result!.Value.Damage);
    }

    [Fact]
    public async Task Cross_context_remote_result_uses_winning_pipeline_options_unless_dispatch_overrides()
    {
        var faults = new List<ResultHookFault>();
        using var server = PluginServer.Create(
            defaultPolicy: TestPolicies.Chain(),
            onResultHookFault: faults.Add);
        var packageA = WithPluginId(CaptureLocalPackage(), "remote-result-options-a");
        var packageB = WithPluginId(CaptureLocalPackage(), "remote-result-options-b");
        var never = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        RemoteLocalResultRequest request = (_, _, _) => new ValueTask<byte[]>(never.Task);

        await InstallRemoteResultAsync(
            server,
            packageA,
            CrossResultContextA.Create,
            priority: 0,
            ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(100),
                new RemoteDamageResult(true, "a-timeout", -1)),
            request);
        await InstallRemoteResultAsync(
            server,
            packageB,
            CrossResultContextB.Create,
            priority: 100,
            ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(100),
                new RemoteDamageResult(true, "b-timeout", -2)),
            request);

        var defaultResult = await server.Hooks.FireAsync(new RemoteDamageContext(12));
        var explicitResult = await server.Hooks.FireAsync<RemoteDamageContext, RemoteDamageResult>(
            new RemoteDamageContext(12),
            ResultHookDispatchOptions<RemoteDamageResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(100),
                new RemoteDamageResult(true, "explicit-timeout", -9)));

        Assert.Equal(-2, defaultResult!.Value.Damage);
        Assert.Equal("b-timeout", defaultResult.Value.Reason);
        Assert.Equal(-9, explicitResult!.Value.Damage);
        Assert.Equal("explicit-timeout", explicitResult.Value.Reason);
        Assert.Equal(2, faults.Count);
        Assert.All(faults, fault => Assert.IsType<TimeoutException>(fault.Exception));
    }

    private static async Task InstallRemoteResultAsync<TContext>(
        PluginServer server,
        PluginPackage package,
        Func<HookContext, TContext> createContext,
        int priority,
        ResultHookDispatchOptions<RemoteDamageResult> options,
        RemoteLocalResultRequest request)
    {
        var kernel = await server.InstallAsync(package).ConfigureAwait(false);
        var subscriptionId = kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;
        server.Hooks.On<RemoteDamageContext, TContext>(createContext)
            .ConfigureResultDispatch(options)
            .UseProjectingResult(
                kernel,
                subscriptionId,
                typeof(RemoteDamageResult),
                request,
                priority);
    }

    private static PluginPackage WithPluginId(PluginPackage package, string pluginId)
    {
        var metadata = package.Module.Metadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        metadata["pluginId"] = pluginId;
        return package with
        {
            Manifest = package.Manifest with { PluginId = pluginId },
            Module = package.Module with
            {
                Id = pluginId,
                Metadata = metadata
            }
        };
    }
}
