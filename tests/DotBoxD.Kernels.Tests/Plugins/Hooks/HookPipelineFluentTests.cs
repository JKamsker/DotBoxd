using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// The fluent hook-chain surface: Where/Select re-type and compose, RunLocal is the native host
/// terminal, and Run(lambda) is the analyzer-lowered terminal that throws until lowered so
/// plugin logic never runs unsandboxed by accident.
/// </summary>
public sealed class HookPipelineFluentTests
{
    private sealed record Ping(string Target, int Value);

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
        using var server = DotBoxD.Plugins.PluginServer.Create(messages);
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
