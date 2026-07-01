using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// The fluent hook-chain surface: Where/Select re-type and compose, RunLocal is the native host
/// terminal, and Run(lambda) is the analyzer-lowered terminal that throws until lowered so
/// plugin logic never runs unsandboxed by accident.
/// </summary>
public sealed class HookPipelineFluentTests
{
    private sealed record Ping(string Target, int Value);

    private sealed record HookServerContext(HookContext Raw, string Prefix);

    private sealed record SubscriptionServerContext(HookContext Raw, int Multiplier);

    private static HookServerContext CreateHookContextA(HookContext context)
        => new(context, "a");

    private static HookServerContext CreateHookContextB(HookContext context)
        => new(context, "b");

    private static SubscriptionServerContext CreateSubscriptionContextA(HookContext context)
        => new(context, 1);

    private static SubscriptionServerContext CreateSubscriptionContextB(HookContext context)
        => new(context, 2);

    private static HookContext Identity(HookContext context) => context;

    [Fact]
    public async Task Select_then_RunLocal_runs_the_native_terminal_with_the_projected_value()
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages);
        server.Hooks.On<Ping>()
            .Select((p, ctx) => p.Value * 2)
            .RunLocal((doubled, ctx) => ctx.Messages.Send("monster-1", "v:" + doubled));

        await server.Hooks.PublishAsync(new Ping("monster-1", 21));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("v:42", message.Message);
    }

    [Fact]
    public async Task Staged_Where_short_circuits_the_terminal()
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = PluginAddendumTestPolicies.CreateServer(messages);
        server.Hooks.On<Ping>()
            .Select((p, ctx) => p.Value)
            .Where((value, ctx) => value >= 100)
            .RunLocal((value, ctx) => ctx.Messages.Send("monster-1", "big"));

        await server.Hooks.PublishAsync(new Ping("monster-1", 5));

        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Publish_with_cancellable_token_exposes_that_token_to_handlers()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;
        server.Hooks.On<Ping>()
            .RunLocal((_, ctx) => observed = ctx.CancellationToken);

        await server.Hooks.PublishAsync(new Ping("monster-1", 21), cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task Precancelled_publish_does_not_run_filters_or_local_handlers()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var filterInvoked = false;
        var handlerInvoked = false;
        server.Hooks.On<Ping>()
            .Where((_, _) => { filterInvoked = true; return true; })
            .RunLocal((_, _) => handlerInvoked = true);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Exception? exception = null;
        try
        {
            await server.Hooks.PublishAsync(new Ping("monster-1", 21), cts.Token);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        Assert.False(filterInvoked);
        Assert.False(handlerInvoked);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task Native_wire_methods_return_the_resolved_event_and_terminal()
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = PluginAddendumTestPolicies.CreateServer(messages);
        server.Events.Resolve<DamageEvent>();
        var kernel = await server.InstallAsync(
            FireDamagePluginPackage.Create(),
            PluginAddendumTestPolicies.LongWall());

        var hook = server.WireHook(kernel);
        var subscription = server.WireSubscription(kernel);

        Assert.Equal(typeof(DamageEvent), hook.EventType);
        Assert.Equal(nameof(DamageEvent), hook.EventName);
        Assert.Equal(KernelWireKind.Plain, hook.Terminal.Kind);
        Assert.Equal(hook, subscription);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 150, "target-1"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("target-1", message.TargetId);
        Assert.Equal("Ouch, fire.", message.Message);
    }

    [Fact]
    public async Task Async_filters_and_handlers_resume_in_order()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var observed = new List<string>();
        server.Hooks.On<Ping>()
            .Where(async (_, _) =>
            {
                await Task.Yield();
                observed.Add("filter-1");
                return true;
            })
            .Where((_, _) =>
            {
                observed.Add("filter-2");
                return true;
            })
            .RunLocal(async (_, _) =>
            {
                await Task.Yield();
                observed.Add("handler-1");
            })
            .RunLocal((_, _) => observed.Add("handler-2"));

        await server.Hooks.PublishAsync(new Ping("monster-1", 21));

        Assert.Equal(["filter-1", "filter-2", "handler-1", "handler-2"], observed);
    }

    [Fact]
    public async Task Async_filter_false_still_short_circuits_handlers()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var handled = false;
        server.Hooks.On<Ping>()
            .Where(async (_, _) =>
            {
                await Task.Yield();
                return false;
            })
            .RunLocal((_, _) => handled = true);

        await server.Hooks.PublishAsync(new Ping("monster-1", 21));

        Assert.False(handled);
    }

    [Fact]
    public async Task Hook_and_subscription_pipelines_can_use_different_server_context_types()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        string? hookObserved = null;
        var subscriptionObserved = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        server.Hooks.On<Ping, HookServerContext>(ctx => new HookServerContext(ctx, "hook"))
            .Where((_, ctx) => ctx.Prefix == "hook")
            .Select((p, ctx) => ctx.Prefix + ":" + p.Target)
            .RunLocal((value, ctx) =>
            {
                Assert.Equal("hook", ctx.Prefix);
                hookObserved = value;
            });

        server.Subscriptions.On<Ping, SubscriptionServerContext>(
                ctx => new SubscriptionServerContext(ctx, Multiplier: 3))
            .Where((_, ctx) => ctx.Multiplier == 3)
            .Select((p, ctx) => p.Value * ctx.Multiplier)
            .RunLocal((value, ctx) =>
            {
                Assert.Equal(3, ctx.Multiplier);
                subscriptionObserved.SetResult(value);
            });

        await server.Hooks.PublishAsync(new Ping("monster-1", 7));
        server.Subscriptions.Publish(new Ping("monster-1", 7));

        Assert.Equal("hook:monster-1", hookObserved);
        Assert.Equal(21, await subscriptionObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Hook_registry_reuses_the_same_context_factory_and_rejects_conflicts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        var first = server.Hooks.On<Ping, HookServerContext>(CreateHookContextA);
        var second = server.Hooks.On<Ping, HookServerContext>(CreateHookContextA);

        Assert.Same(first, second);
        var exception = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping, HookServerContext>(CreateHookContextB));
        Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK067");
    }

    [Fact]
    public void Subscription_registry_reuses_the_same_context_factory_and_rejects_conflicts()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        var first = server.Subscriptions.On<Ping, SubscriptionServerContext>(CreateSubscriptionContextA);
        var second = server.Subscriptions.On<Ping, SubscriptionServerContext>(CreateSubscriptionContextA);

        Assert.Same(first, second);
        var exception = Assert.Throws<SandboxValidationException>(
            () => server.Subscriptions.On<Ping, SubscriptionServerContext>(CreateSubscriptionContextB));
        Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK067");
    }

    [Fact]
    public void HookContext_overload_order_fails_deterministically_instead_of_invalid_casting()
    {
        using (var server = DotBoxD.Plugins.PluginServer.Create())
        {
            _ = server.Hooks.On<Ping>();

            var exception = Assert.Throws<SandboxValidationException>(
                () => server.Hooks.On<Ping, HookContext>(Identity));

            Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK067");
        }

        using (var server = DotBoxD.Plugins.PluginServer.Create())
        {
            _ = server.Hooks.On<Ping, HookContext>(Identity);

            var exception = Assert.Throws<SandboxValidationException>(
                () => server.Hooks.On<Ping>());

            Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK067");
        }
    }

    [Fact]
    public void Subscription_HookContext_overload_order_fails_deterministically_instead_of_invalid_casting()
    {
        using (var server = DotBoxD.Plugins.PluginServer.Create())
        {
            _ = server.Subscriptions.On<Ping>();

            var exception = Assert.Throws<SandboxValidationException>(
                () => server.Subscriptions.On<Ping, HookContext>(Identity));

            Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK067");
        }

        using (var server = DotBoxD.Plugins.PluginServer.Create())
        {
            _ = server.Subscriptions.On<Ping, HookContext>(Identity);

            var exception = Assert.Throws<SandboxValidationException>(
                () => server.Subscriptions.On<Ping>());

            Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK067");
        }
    }


    [Fact]
    public void Run_lambda_throws_until_lowered()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>().Run((p, ctx) => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Staged_Run_lambda_throws_until_lowered()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>()
                .Select((p, ctx) => p.Value)
                .Run((value, ctx) => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Element_only_Run_func_throws_until_lowered()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        // The element-only Func<TEvent, ValueTask> terminal must throw just like the (e, ctx) form: it
        // can never run as host code (a verified terminal is reached only through the lowered IR).
        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>().Run(p => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Element_only_Run_action_throws_until_lowered()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>().Run(p => { }));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Staged_element_only_Run_throws_until_lowered()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>()
                .Select(p => p.Value)
                .Run(value => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }
}
