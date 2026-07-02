using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

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

    private sealed record ReferenceDamageResult(bool Success, string? Reason) : IHookResult;

    private sealed record DamageServerContext(HookContext Raw);

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
            () => pipeline.RegisterLocal<DamageResult>(
                (DamageCtx c, HookContext ctx) => default(DamageResult),
                priority: 0));
        Assert.Throws<SandboxValidationException>(
            () => pipeline.RegisterLocal<DamageResult>(
                (DamageCtx c, CancellationToken ct) => new ValueTask<DamageResult>(default(DamageResult)),
                priority: 0));
    }

    [Fact]
    public void Typed_context_Register_and_RegisterLocal_overloads_throw_until_lowered()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<DamageCtx, DamageServerContext>(
            new StubAdapter(),
            ctx => new DamageServerContext(ctx));

        Assert.Throws<SandboxValidationException>(
            () => pipeline.Register<DamageResult>((c, ctx) => default, priority: 0));
        Assert.Throws<SandboxValidationException>(
            () => pipeline.RegisterLocal<DamageResult>(
                (DamageCtx c, DamageServerContext ctx) => default(DamageResult),
                priority: 0));
        Assert.Throws<SandboxValidationException>(
            () => pipeline.RegisterLocal<DamageResult>(
                (DamageCtx c, DamageServerContext ctx, CancellationToken ct) =>
                    new ValueTask<DamageResult>(default(DamageResult)),
                priority: 0));
    }

    [Fact]
    public void Typed_remote_stage_result_terminals_throw_until_lowered()
    {
        var hooks = new RemoteHookRegistry(_ => ValueTask.FromResult("unused"));
        var stage = hooks.On<DamageCtx, DamageServerContext>(ctx => new DamageServerContext(ctx))
            .Select((c, _) => c.Damage);

        Assert.Throws<InvalidOperationException>(() =>
        {
            stage.Register<DamageResult>(damage => default, priority: 0);
        });
        Assert.Throws<NotSupportedException>(() =>
        {
            stage.RegisterLocal<DamageResult>(
                (int damage, DamageServerContext ctx) => default(DamageResult),
                priority: 0);
        });
    }

    [Fact]
    public async Task FireAsync_returns_null_when_no_hook_point_exists()
    {
        using var server = PluginServer.Create();

        var result = await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10));

        Assert.Null(result);
    }

    [Fact]
    public async Task FireAsync_observes_precanceled_token_when_no_hook_point_exists()
    {
        using var server = PluginServer.Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10), cts.Token));
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
    public async Task FireAsync_observes_precanceled_token_when_a_hook_point_has_no_result_handlers()
    {
        using var server = PluginServer.Create();
        server.Hooks.On<DamageCtx>(new StubAdapter());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10), cts.Token));
    }

    [Fact]
    public async Task FireAsync_observes_precanceled_token_when_multiple_hook_points_have_no_result_handlers()
    {
        using var server = PluginServer.Create();
        var adapter = new StubAdapter();
        server.Hooks.On<DamageCtx>(adapter);
        server.Hooks.On<DamageCtx, DamageServerContext>(
            adapter,
            ctx => new DamageServerContext(ctx));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await server.Hooks.FireAsync<DamageCtx, DamageResult>(new DamageCtx(10), cts.Token));
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

    [Fact]
    public async Task Type_based_result_installs_reject_reference_hook_results()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(ReferenceValidationPackage());
        var pipeline = server.Hooks.On<DamageCtx>(new StubAdapter());
        RemoteLocalResultRequest request = (_, _, _) => new ValueTask<byte[]>([]);

        var useResult = Assert.Throws<ArgumentException>(
            () => pipeline.UseResult(kernel, typeof(ReferenceDamageResult)));
        var useProjecting = Assert.Throws<ArgumentException>(
            () => pipeline.UseProjectingResult(kernel, "subscription-id", typeof(ReferenceDamageResult), request));

        Assert.Contains("value type", useResult.Message, StringComparison.Ordinal);
        Assert.Contains("value type", useProjecting.Message, StringComparison.Ordinal);
    }

    private static PluginPackage ReferenceValidationPackage()
        => PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record KernelEvent(string TargetId, string Message);

            [Plugin("reference-result-validation")]
            public sealed partial class DamageKernel : IEventKernel<KernelEvent>
            {
                public bool ShouldHandle(KernelEvent e, HookContext ctx) => true;

                public void Handle(KernelEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

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
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
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
