using DotBoxD.Abstractions;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public enum CombatRelation
{
    Pve = 0,
    Pvp = 1,
}

[Hook("combat.damage", typeof(CombatDamageResult))]
public sealed record CombatDamageContext(CombatRelation Relation, int Damage);

[HookResult]
public readonly partial record struct CombatDamageResult(bool Success, string? Reason, int Damage);

[Hook("combat.optional-damage", typeof(OptionalDamageResult))]
public sealed record OptionalDamageContext(int Damage, bool CanDie);

[HookResult]
public readonly partial record struct OptionalDamageResult(bool Success, string? Reason, int? Damage, bool? CanDie);

/// <summary>
/// End-to-end coverage for result-hook lowering: the <c>On&lt;TContext&gt;().Where(...).Register/RegisterLocal</c>
/// chains are authored as ordinary code, lowered by the DotBoxD generator loaded as a real build-time analyzer,
/// and intercepted into the live server. A passing FireAsync also proves interception ran — un-lowered, the
/// Register/RegisterLocal terminals throw.
/// </summary>
public sealed class ResultHookChainTests
{
    [Fact]
    public async Task Register_lowers_the_handler_and_returns_the_constructed_result()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => new CombatDamageResult { Success = true, Damage = ctx.Damage * 2 }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.NotNull(result);
        Assert.True(result!.Value.Success);
        Assert.Equal(100, result.Value.Damage);
    }

    [Fact]
    public async Task Register_lowers_the_fluent_builder_chain()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 10);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 25));

        Assert.True(result!.Value.Success);
        Assert.Equal(50, result.Value.Damage);
    }

    [Fact]
    public async Task Register_round_trips_nullable_scalar_result_fields()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<OptionalDamageContext>()
            .Register(
                ctx => OptionalDamageResult.Ok()
                    .WithDamage(ctx.Damage)
                    .WithCanDie(null),
                priority: 10);

        var result = await server.Hooks.FireAsync<OptionalDamageContext, OptionalDamageResult>(
            new OptionalDamageContext(15, true));

        Assert.True(result!.Value.Success);
        Assert.Equal(15, result.Value.Damage);
        Assert.Null(result.Value.CanDie);
    }

    [Fact]
    public async Task Register_defaults_omitted_nullable_scalar_result_fields_to_null()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<OptionalDamageContext>()
            .Register(ctx => new OptionalDamageResult { Success = true }, priority: 10);

        var result = await server.Hooks.FireAsync<OptionalDamageContext, OptionalDamageResult>(
            new OptionalDamageContext(15, true));

        Assert.True(result!.Value.Success);
        Assert.Null(result.Value.Damage);
        Assert.Null(result.Value.CanDie);
    }

    [Fact]
    public async Task Register_reject_builder_abstains_to_the_next_handler()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => CombatDamageResult.Reject("nope"), priority: 100);
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => CombatDamageResult.Ok().WithDamage(1), priority: 0);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 25));

        Assert.True(result!.Value.Success);
        Assert.Equal(1, result.Value.Damage);
    }

    [Fact]
    public async Task Register_filter_that_does_not_match_yields_no_result()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => new CombatDamageResult { Success = true, Damage = ctx.Damage * 2 }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pvp, 50));

        Assert.Null(result);
    }

    [Fact]
    public async Task Higher_priority_register_wins_over_lower()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = true, Damage = 1 }, priority: 0);
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = true, Damage = 999 }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.Equal(999, result!.Value.Damage);
    }

    [Fact]
    public async Task Abstaining_register_falls_through_to_the_next()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = false, Reason = "abstain" }, priority: 100);
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = true, Damage = 7 }, priority: 0);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.True(result!.Value.Success);
        Assert.Equal(7, result.Value.Damage);
    }

    [Fact]
    public async Task RegisterLocal_runs_the_in_process_delegate_only_when_the_filter_matches()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var invoked = 0;
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pvp)
            .RegisterLocal((ctx, hookContext) =>
            {
                invoked++;
                return new CombatDamageResult { Success = true, Damage = ctx.Damage + 1 };
            }, priority: 50);

        var miss = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 10));
        Assert.Null(miss);
        Assert.Equal(0, invoked);

        var hit = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pvp, 10));
        Assert.Equal(11, hit!.Value.Damage);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task RegisterLocal_cancellation_aware_overload_threads_the_dispatch_token()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var invoked = 0;
        CancellationToken received = default;
        using var cts = new CancellationTokenSource();
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .RegisterLocal((ctx, hookContext, cancellationToken) =>
            {
                received = cancellationToken;
                cancellationToken.ThrowIfCancellationRequested();
                invoked++;
                return new ValueTask<CombatDamageResult>(
                    new CombatDamageResult { Success = true, Damage = ctx.Damage + 2 });
            }, priority: 50);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 10), cts.Token);

        Assert.Equal(12, result!.Value.Damage);
        Assert.Equal(1, invoked);
        // The token the handler observed is the dispatch token, not CancellationToken.None: it threads through
        // FireAsync -> HookContext -> the handler's 3rd parameter. (Previously fired with None, which left the
        // handler's ThrowIfCancellationRequested dead and proved nothing about token threading.)
        Assert.True(received.CanBeCanceled);
        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public void Register_install_entrypoint_carries_no_handler_delegate()
    {
        // Pins the sandbox-verified Register contract structurally: UseGeneratedResultChain (the entrypoint the
        // generated Register interceptor calls) installs from the package + priority ONLY — it has no delegate
        // parameter, so a Register handler body can never be captured and run in-process. UseGeneratedLocalResultChain
        // is the deliberate exception: it threads the plugin-process handler. A regression that added a handler
        // argument to the Register entrypoint (and ran it in-process) would fail here.
        var pipeline = typeof(HookPipeline<>);

        var register = pipeline.GetMethods().Where(m => m.Name == "UseGeneratedResultChain").ToArray();
        Assert.NotEmpty(register);
        Assert.All(register, m => Assert.DoesNotContain(m.GetParameters(), IsDelegateParameter));

        var local = pipeline.GetMethods().Where(m => m.Name == "UseGeneratedLocalResultChain").ToArray();
        Assert.NotEmpty(local);
        Assert.All(local, m => Assert.Contains(m.GetParameters(), IsDelegateParameter));
    }

    [Fact]
    public async Task Lowered_and_local_Ok_pin_the_omitted_reason_convention()
    {
        // Pins the documented Reason representation convention across the two transports. A successful result is
        // the only place Reason is observable (dispatch drops abstain results), so Ok() is used to compare:
        //   - sandbox-lowered Register fills the omitted Reason with the IR's non-null string zero ("")
        //   - in-process RegisterLocal runs the generated builder, leaving Reason at its C# default (null)
        // Reason is otherwise never surfaced, so this gap is benign today; the test guards against silent drift if
        // a future change surfaces Reason.
        using var lowered = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        lowered.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage), priority: 0);

        var sandbox = await lowered.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 5));
        Assert.True(sandbox!.Value.Success);
        Assert.Equal(string.Empty, sandbox.Value.Reason);

        using var local = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        local.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .RegisterLocal((ctx, _) => CombatDamageResult.Ok().WithDamage(ctx.Damage), priority: 0);

        var inProcess = await local.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 5));
        Assert.True(inProcess!.Value.Success);
        Assert.Null(inProcess.Value.Reason);
    }

    [Fact]
    public async Task Uninstalling_a_lowered_Register_chain_prunes_the_result_slot()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => new CombatDamageResult { Success = true, Damage = ctx.Damage * 2 }, priority: 100);

        var ctx = new CombatDamageContext(CombatRelation.Pve, 50);
        var pipeline = server.Hooks.On<CombatDamageContext>(); // the same cached pipeline the chain installed into

        var before = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(ctx);
        Assert.Equal(100, before!.Value.Damage);
        Assert.Equal(1, ResultEntryCount(pipeline));

        // Uninstalling the lowered chain must prune ResultHookSlot via HookPipeline.RemoveKernel. The behavioral
        // null after revocation is fail-safe regardless (a revoked kernel projects NotMatched and abstains), so the
        // entry-count assertion is the real guard against a regression that dropped _resultHooks.RemoveKernel.
        var installed = Assert.Single(server.Kernels);
        Assert.True(server.Uninstall(installed.Manifest.PluginId));

        Assert.Equal(0, ResultEntryCount(pipeline));
        var after = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(ctx);
        Assert.Null(after);
    }

    private static bool IsDelegateParameter(System.Reflection.ParameterInfo parameter)
        => typeof(Delegate).IsAssignableFrom(parameter.ParameterType)
            || parameter.ParameterType.Name.StartsWith("Func", StringComparison.Ordinal)
            || parameter.ParameterType.Name.StartsWith("Action", StringComparison.Ordinal);

    private static int ResultEntryCount<TEvent>(HookPipeline<TEvent> pipeline)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var slotField = typeof(HookPipeline<TEvent>).GetField("_resultHooks", flags);
        Assert.NotNull(slotField);
        var slot = slotField!.GetValue(pipeline)!;
        var entriesField = slot.GetType().GetField("_entries", flags);
        Assert.NotNull(entriesField);
        var entries = (Array)entriesField!.GetValue(slot)!;
        return entries.Length;
    }
}
