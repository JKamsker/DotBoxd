using DotBoxD.Abstractions;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests.Contexts;

public sealed class TypedServerContextTests
{
    [Fact]
    public async Task Local_hook_Run_lowers_typed_context_method_and_property()
    {
        var state = new TypedContextBindingState();
        using var server = CreateServer(state, read: true, write: true);

        server.Hooks.On<TypedSignalEvent, TypedHookServerContext>(TypedHookServerContext.Create)
            .Where((e, ctx) => ctx.IsNear(e.Distance))
            .Select((e, ctx) => ctx.Tag(e.TargetId))
            .Run((targetId, ctx) => ctx.Deliver(targetId, ctx.Label));

        await server.Hooks.PublishAsync(new TypedSignalEvent("alpha", 3));
        await server.Hooks.PublishAsync(new TypedSignalEvent("far", 9));

        Assert.Equal("alpha:hook|" + TypedContextBindings.HookLabel, Assert.Single(state.HookDeliveries));
    }

    [Fact]
    public async Task Local_hook_Register_and_RegisterLocal_consume_typed_contexts()
    {
        var sandboxState = new TypedContextBindingState();
        using (var server = CreateServer(sandboxState, read: true))
        {
            server.Hooks.On<TypedDamageContext, TypedHookServerContext>(TypedHookServerContext.Create)
                .Where((e, ctx) => ctx.IsNear(e.Distance))
                .Register((e, ctx) => new TypedDamageResult
                {
                    Success = true,
                    Reason = ctx.Label,
                    Damage = e.Damage + ctx.Adjustment,
                });

            var result = await server.Hooks.FireAsync<TypedDamageContext, TypedDamageResult>(
                new TypedDamageContext("target", 3, 5));

            Assert.True(result!.Value.Success);
            Assert.Equal(TypedContextBindings.HookLabel, result.Value.Reason);
            Assert.Equal(12, result.Value.Damage);
        }

        using (var server = CreateServer(new TypedContextBindingState()))
        {
            server.Hooks.On<TypedDamageContext, TypedHookServerContext>(TypedHookServerContext.Create)
                .Where((e, ctx) => ctx.IsNear(e.Distance))
                .RegisterLocal((e, ctx) => new TypedDamageResult
                {
                    Success = true,
                    Reason = ctx.NativeLabel,
                    Damage = e.Damage + ctx.NativeAdjustment,
                });

            var miss = await server.Hooks.FireAsync<TypedDamageContext, TypedDamageResult>(
                new TypedDamageContext("far", 9, 5));
            var hit = await server.Hooks.FireAsync<TypedDamageContext, TypedDamageResult>(
                new TypedDamageContext("near", 3, 5));

            Assert.Null(miss);
            Assert.Equal("hook-native", hit!.Value.Reason);
            Assert.Equal(16, hit.Value.Damage);
        }
    }

    [Fact]
    public async Task Remote_hook_Run_and_RunLocal_carry_the_configured_context()
    {
        var runState = new TypedContextBindingState();
        using (var server = CreateServer(runState, read: true, write: true))
        {
            var hooks = RemoteHookRegistryForRun<TypedSignalEvent>(server);
            hooks.On<TypedSignalEvent, TypedHookServerContext>(TypedHookServerContext.Create)
                .Where((e, ctx) => ctx.IsNear(e.Distance))
                .Select((e, ctx) => ctx.Tag(e.TargetId))
                .Run((targetId, ctx) => ctx.Deliver(targetId, ctx.Label));

            await server.Hooks.PublishAsync(new TypedSignalEvent("remote", 3));

            Assert.Equal("remote:hook|" + TypedContextBindings.HookLabel, Assert.Single(runState.HookDeliveries));
        }

        var received = new List<string>();
        using (var harness = new RunLocalHarness<TypedSignalEvent>())
        {
            harness.Hooks.On<TypedSignalEvent, TypedHookServerContext>(TypedHookServerContext.Create)
                .Where((e, ctx) => ctx.IsNear(e.Distance))
                .Select((e, ctx) => ctx.Tag(e.TargetId))
                .RunLocal((targetId, ctx) => received.Add(targetId + "|" + ctx.NativeLabel));

            await harness.PublishAsync(new TypedSignalEvent("local", 3));
            await harness.PublishAsync(new TypedSignalEvent("far", 9));
        }

        Assert.Equal("local:hook|hook-native", Assert.Single(received));
    }

    [Fact]
    public async Task Remote_hook_Register_and_RegisterLocal_carry_the_configured_context()
    {
        var sandboxState = new TypedContextBindingState();
        using (var server = CreateServer(sandboxState, read: true))
        {
            var hooks = RemoteHookRegistryForResult<TypedDamageContext, TypedDamageResult>(server);
            hooks.On<TypedDamageContext, TypedHookServerContext>(TypedHookServerContext.Create)
                .Where((e, ctx) => ctx.IsNear(e.Distance))
                .Register((e, ctx) => new TypedDamageResult
                {
                    Success = true,
                    Reason = ctx.Label,
                    Damage = e.Damage + ctx.Adjustment,
                });

            var result = await server.Hooks.FireAsync<TypedDamageContext, TypedDamageResult>(
                new TypedDamageContext("remote", 3, 5));

            Assert.Equal(TypedContextBindings.HookLabel, result!.Value.Reason);
            Assert.Equal(12, result.Value.Damage);
        }

        using (var server = CreateServer(new TypedContextBindingState()))
        {
            var localHandlers = new RemoteLocalHandlerRegistry();
            var hooks = RemoteHookRegistryForResult<TypedDamageContext, TypedDamageResult>(server, localHandlers);
            hooks.On<TypedDamageContext, TypedHookServerContext>(TypedHookServerContext.Create)
                .Where((e, ctx) => ctx.IsNear(e.Distance))
                .RegisterLocal((e, ctx) => new TypedDamageResult
                {
                    Success = true,
                    Reason = ctx.NativeLabel,
                    Damage = e.Damage + ctx.NativeAdjustment,
                });

            var miss = await server.Hooks.FireAsync<TypedDamageContext, TypedDamageResult>(
                new TypedDamageContext("far", 9, 5));
            var hit = await server.Hooks.FireAsync<TypedDamageContext, TypedDamageResult>(
                new TypedDamageContext("near", 3, 5));

            Assert.Null(miss);
            Assert.Equal("hook-native-cancelable", hit!.Value.Reason);
            Assert.Equal(16, hit.Value.Damage);
        }
    }

    [Fact]
    public async Task Local_subscription_context_is_independent_from_hook_context()
    {
        var state = new TypedContextBindingState();
        using var server = CreateServer(state, read: true, write: true);
        var local = new List<string>();

        server.Subscriptions.On<TypedSignalEvent, TypedSubscriptionServerContext>(TypedSubscriptionServerContext.Create)
            .Where((e, ctx) => ctx.ShouldReceive(e.Distance))
            .Select((e, ctx) => ctx.Tag(e.TargetId))
            .Run((targetId, ctx) => ctx.Deliver(targetId, ctx.Label));

        server.Subscriptions.On<TypedSignalEvent, TypedSubscriptionServerContext>(TypedSubscriptionServerContext.Create)
            .Where((e, ctx) => ctx.ShouldReceive(e.Distance))
            .RunLocal((e, ctx) => local.Add(e.TargetId + "|" + ctx.NativeLabel));

        server.Subscriptions.Publish(new TypedSignalEvent("sub", 2));
        server.Subscriptions.Publish(new TypedSignalEvent("far", 9));
        await WaitForAsync(() => state.SubscriptionDeliveries.Count == 1 && local.Count == 1);

        Assert.Equal("sub:subscription|" + TypedContextBindings.SubscriptionLabel,
            Assert.Single(state.SubscriptionDeliveries));
        Assert.Equal("sub|subscription-native", Assert.Single(local));
        Assert.Empty(state.HookDeliveries);
    }

    [Fact]
    public async Task Remote_subscription_Run_and_RunLocal_carry_the_configured_context()
    {
        var runState = new TypedContextBindingState();
        using (var server = CreateServer(runState, read: true, write: true))
        {
            var subscriptions = RemoteSubscriptionRegistryForRun<TypedSignalEvent>(server);
            subscriptions.On<TypedSignalEvent, TypedSubscriptionServerContext>(TypedSubscriptionServerContext.Create)
                .Where((e, ctx) => ctx.ShouldReceive(e.Distance))
                .Select((e, ctx) => ctx.Tag(e.TargetId))
                .Run((targetId, ctx) => ctx.Deliver(targetId, ctx.Label));

            server.Subscriptions.Publish(new TypedSignalEvent("remote-sub", 2));
            await WaitForAsync(() => runState.SubscriptionDeliveries.Count == 1);

            Assert.Equal("remote-sub:subscription|" + TypedContextBindings.SubscriptionLabel,
                Assert.Single(runState.SubscriptionDeliveries));
        }

        var received = new List<string>();
        using (var harness = new SubscriptionHarness<TypedSignalEvent>())
        {
            harness.Subscriptions.On<TypedSignalEvent, TypedSubscriptionServerContext>(TypedSubscriptionServerContext.Create)
                .Where((e, ctx) => ctx.ShouldReceive(e.Distance))
                .Select((e, ctx) => ctx.Tag(e.TargetId))
                .RunLocal((targetId, ctx) => received.Add(targetId + "|" + ctx.NativeLabel));

            harness.Publish(new TypedSignalEvent("local-sub", 2));
            harness.Publish(new TypedSignalEvent("far", 9));
            await WaitForAsync(() => received.Count == 1);
        }

        Assert.Equal("local-sub:subscription|subscription-native", Assert.Single(received));
    }

    private static PluginServer CreateServer(TypedContextBindingState state, bool read = false, bool write = false)
        => PluginServer.Create(
            configureHost: builder => TypedContextBindings.AddBindings(builder, state),
            defaultPolicy: TypedContextBindings.Policy(read, write));

    private static RemoteHookRegistry RemoteHookRegistryForRun<TEvent>(PluginServer server)
        => new(async package =>
        {
            var kernel = await server.InstallAsync(package).ConfigureAwait(false);
            server.Hooks.On<TEvent>().Use(kernel);
            return package.Manifest.PluginId;
        });

    private static RemoteHookRegistry RemoteHookRegistryForResult<TEvent, TResult>(
        PluginServer server,
        RemoteLocalHandlerRegistry? localHandlers = null)
        where TResult : struct, IHookResult
        => new(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscription = Assert.Single(package.Manifest.Subscriptions);
                var pipeline = server.Hooks.On<TEvent>();
                if (subscription.ResultLocalTerminal)
                {
                    var handlers = localHandlers ?? throw new InvalidOperationException("Local handlers required.");
                    var subscriptionId = kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;
                    pipeline.UseProjectingResult(
                        kernel,
                        subscriptionId,
                        typeof(TResult),
                        (id, payload, token) => handlers.DispatchResultAsync(
                            id,
                            payload.ToArray(),
                            new HookContext(new InMemoryPluginMessageSink(), token),
                            token),
                        subscription.Priority);
                }
                else
                {
                    pipeline.UseResult(kernel, typeof(TResult), subscription.Priority);
                }

                return kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;
            },
            localHandlers);

    private static RemoteSubscriptionRegistry RemoteSubscriptionRegistryForRun<TEvent>(PluginServer server)
        => new(async package =>
        {
            var kernel = await server.InstallAsync(package).ConfigureAwait(false);
            server.Subscriptions.On<TEvent>().Use(kernel);
            return package.Manifest.PluginId;
        });

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.True(condition(), "Timed out waiting for typed context callback.");
            }

            await Task.Delay(10);
        }
    }
}
