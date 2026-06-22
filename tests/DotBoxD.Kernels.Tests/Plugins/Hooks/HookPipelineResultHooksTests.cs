using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// The result-hook facade on the host hook surface: the Register/RegisterLocal terminals throw until the
/// analyzer lowers them (so plugin logic never runs unsandboxed by accident), and FireAsync returns null when
/// nothing is registered.
/// </summary>
public sealed class HookPipelineResultHooksTests
{
    [Hook("test.damage", typeof(DamageResult))]
    private sealed record DamageCtx(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    private readonly record struct OtherDamageResult(bool Success, string? Reason) : IHookResult;

    [Fact]
    public void Hook_attribute_rejects_invalid_constructor_arguments()
    {
        Assert.Throws<ArgumentException>(() => new HookAttribute("", typeof(DamageResult)));
        Assert.Throws<ArgumentException>(() => new HookAttribute(" ", typeof(DamageResult)));
        Assert.Throws<ArgumentNullException>(() => new HookAttribute("test.damage", null!));
    }

    [Fact]
    public void Polymorphic_handle_attributes_reject_invalid_constructor_arguments()
    {
        Assert.Throws<ArgumentException>(() => new PolymorphicHandleAttribute(""));
        Assert.Throws<ArgumentException>(() => new PolymorphicHandleAttribute(" "));
        Assert.Throws<ArgumentNullException>(
            () => new HandleSubtypeAttribute(null!, "player", "combatant.player", "combatant.player.read"));
        Assert.Throws<ArgumentException>(
            () => new HandleSubtypeAttribute(typeof(DamageCtx), "", "combatant.player", "combatant.player.read"));
        Assert.Throws<ArgumentException>(
            () => new HandleSubtypeAttribute(typeof(DamageCtx), "player", "", "combatant.player.read"));
        Assert.Throws<ArgumentException>(
            () => new HandleSubtypeAttribute(typeof(DamageCtx), "player", "combatant.player", ""));
    }

    [Fact]
    public void Register_throws_until_lowered()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());

        Assert.Throws<SandboxValidationException>(() => pipeline.Register<DamageResult>(c => default, priority: 0));
    }

    [Fact]
    public void RegisterLocal_throws_until_lowered()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());

        Assert.Throws<SandboxValidationException>(
            () => pipeline.RegisterLocal<DamageResult>((c, ctx) => default, priority: 0));
    }

    [Fact]
    public async Task FireAsync_returns_null_when_no_hook_point_exists()
    {
        using var server = PluginServer.Create();

        var result = await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10));

        Assert.Null(result);
    }

    [Fact]
    public async Task FireAsync_returns_null_when_a_hook_point_has_no_result_handlers()
    {
        using var server = PluginServer.Create();
        server.Hooks.On<DamageCtx>(new StubAdapter());

        var result = await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10));

        Assert.Null(result);
    }

    [Fact]
    public async Task FireAsync_rejects_result_type_that_does_not_match_hook_contract()
    {
        using var server = PluginServer.Create();
        server.Hooks.On<DamageCtx>(new StubAdapter());

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.Hooks.FireAsync<DamageCtx, OtherDamageResult>(new DamageCtx(10)));

        Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK066");
    }

    [Fact]
    public async Task FireAsync_rejects_result_type_when_no_hook_point_exists()
    {
        using var server = PluginServer.Create();

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.Hooks.FireAsync<DamageCtx, OtherDamageResult>(new DamageCtx(10)));

        Assert.Contains(exception.Diagnostics, d => d.Code == "DBXK066");
    }

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}

/// <summary>
/// Pins the spec's "no-handler path returns null and must not allocate" contract for result-hook dispatch.
/// Runs serially (allocation measurement) so a concurrent test thread's allocations cannot contaminate the
/// per-thread sample. Guards both the synchronous no-handler ValueTask fast path AND the cached [Hook] lookup in
/// HookRegistry.FireAsync — a regression that reintroduced a per-dispatch reflection lookup would fail here.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ResultHookFastPathAllocationTests
{
    [Hook("test.damage", typeof(DamageResult))]
    private sealed record DamageCtx(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    [Fact]
    public async Task No_result_handler_dispatch_does_not_allocate()
    {
        using var server = PluginServer.Create();
        server.Hooks.On<DamageCtx>(new StubAdapter());
        var e = new DamageCtx(10);

        // Warm up the JIT and the one-time ResultTypeCache<DamageCtx> static initialization before measuring.
        for (var i = 0; i < 50; i++)
        {
            await server.Hooks.FireAsync<DamageCtx, DamageResult>(e);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            await server.Hooks.FireAsync<DamageCtx, DamageResult>(e);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}
