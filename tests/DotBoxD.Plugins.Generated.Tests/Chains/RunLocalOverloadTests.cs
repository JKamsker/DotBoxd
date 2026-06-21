namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted coverage for every <c>RunLocal</c> terminal overload: the element-and-context and
/// element-only <c>Action</c> forms, plus their async <c>Func&lt;…, ValueTask&gt;</c> counterparts. Each call site is
/// a distinct shape the interceptor must recognize and wire to the native delegate.
/// </summary>
public sealed class RunLocalOverloadTests
{
    [Fact]
    public async Task Action_with_element_and_context_overload_is_intercepted()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal("m-1", Assert.Single(received));
    }

    [Fact]
    public async Task Element_only_action_overload_is_intercepted()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal(id => received.Add(id));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal("m-1", Assert.Single(received));
    }

    [Fact]
    public async Task Async_element_and_context_overload_is_intercepted()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal(async (id, ctx) =>
            {
                await Task.Yield();
                received.Add(id);
            });

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal("m-1", Assert.Single(received));
    }

    [Fact]
    public async Task Async_element_only_overload_is_intercepted()
    {
        var received = new List<string>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal(async id =>
            {
                await Task.Yield();
                received.Add(id);
            });

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal("m-1", Assert.Single(received));
    }

    [Fact]
    public async Task Whole_event_element_only_overload_is_intercepted()
    {
        var received = new List<ChainAggroEvent>();
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal(e => received.Add(e));

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal(new ChainAggroEvent("m-1", 3), Assert.Single(received));
    }
}
