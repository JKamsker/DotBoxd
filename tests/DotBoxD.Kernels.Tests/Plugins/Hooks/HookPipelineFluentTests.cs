using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// The fluent hook-chain surface: Where/Select re-type and compose, InvokeLocal is the native host
/// terminal, and InvokeKernel(lambda) is the analyzer-lowered terminal that throws until lowered so
/// plugin logic never runs unsandboxed by accident.
/// </summary>
public sealed class HookPipelineFluentTests
{
    private sealed record Ping(string Target, int Value);

    [Fact]
    public async Task Select_then_InvokeLocal_runs_the_native_terminal_with_the_projected_value()
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages);
        server.Hooks.On<Ping>()
            .Select((p, ctx) => p.Value * 2)
            .InvokeLocal((doubled, ctx) => ctx.Messages.Send("monster-1", "v:" + doubled));

        await server.Hooks.PublishAsync(new Ping("monster-1", 21));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("v:42", message.Message);
    }

    [Fact]
    public async Task Staged_Where_short_circuits_the_terminal()
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(messages);
        server.Hooks.On<Ping>()
            .Select((p, ctx) => p.Value)
            .Where((value, ctx) => value >= 100)
            .InvokeLocal((value, ctx) => ctx.Messages.Send("monster-1", "big"));

        await server.Hooks.PublishAsync(new Ping("monster-1", 5));

        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Publish_with_cancellable_token_exposes_that_token_to_handlers()
    {
        using var server = PluginServer.Create();
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;
        server.Hooks.On<Ping>()
            .InvokeLocal((_, ctx) => observed = ctx.CancellationToken);

        await server.Hooks.PublishAsync(new Ping("monster-1", 21), cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public void InvokeKernel_lambda_throws_until_lowered()
    {
        using var server = PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>().InvokeKernel((p, ctx) => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Staged_InvokeKernel_lambda_throws_until_lowered()
    {
        using var server = PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>()
                .Select((p, ctx) => p.Value)
                .InvokeKernel((value, ctx) => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Element_only_InvokeKernel_func_throws_until_lowered()
    {
        using var server = PluginServer.Create();

        // The element-only Func<TEvent, ValueTask> terminal must throw just like the (e, ctx) form: it
        // can never run as host code (a verified terminal is reached only through the lowered IR).
        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>().InvokeKernel(p => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Element_only_InvokeKernel_action_throws_until_lowered()
    {
        using var server = PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>().InvokeKernel(p => { }));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }

    [Fact]
    public void Staged_element_only_InvokeKernel_throws_until_lowered()
    {
        using var server = PluginServer.Create();

        var ex = Assert.Throws<SandboxValidationException>(
            () => server.Hooks.On<Ping>()
                .Select(p => p.Value)
                .InvokeKernel(value => ValueTask.CompletedTask));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK062");
    }
}
