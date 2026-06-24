using DotBoxD.Abstractions;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public sealed partial class ResultHookChainTests
{
    public sealed record ResultServerContext(HookContext Raw, int Bonus);

    [Fact]
    public async Task Register_with_typed_server_context_lowers_and_returns_result()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext, ResultServerContext>(
                raw => new ResultServerContext(raw, Bonus: 7))
            .Where((ctx, _) => ctx.Relation == CombatRelation.Pve)
            .Register((ctx, _) => new CombatDamageResult
            {
                Success = true,
                Damage = ctx.Damage * 2
            }, priority: 12);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 21));

        Assert.True(result!.Value.Success);
        Assert.Equal(42, result.Value.Damage);
    }

    [Fact]
    public async Task RegisterLocal_with_typed_server_context_invokes_the_configured_context_type()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var observedBonus = 0;
        server.Hooks.On<CombatDamageContext, ResultServerContext>(
                raw => new ResultServerContext(raw, Bonus: 7))
            .Where((ctx, _) => ctx.Relation == CombatRelation.Pve)
            .RegisterLocal((ctx, serverContext) =>
            {
                observedBonus = serverContext.Bonus;
                return new CombatDamageResult
                {
                    Success = true,
                    Damage = ctx.Damage + serverContext.Bonus
                };
            }, priority: 12);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 21));

        Assert.True(result!.Value.Success);
        Assert.Equal(28, result.Value.Damage);
        Assert.Equal(7, observedBonus);
    }

    [Fact]
    public async Task RegisterLocal_value_only_overload_lowers_and_runs_the_handler()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .RegisterLocal(ctx => new CombatDamageResult
            {
                Success = true,
                Damage = ctx.Damage + 1
            }, priority: 12);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 21));

        Assert.True(result!.Value.Success);
        Assert.Equal(22, result.Value.Damage);
    }

    [Fact]
    public async Task RegisterLocal_cancellation_token_overload_lowers_and_receives_dispatch_token()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        using var dispatch = new CancellationTokenSource();
        CancellationToken observed = default;
        var observedBonus = 0;

        server.Hooks.On<CombatDamageContext, ResultServerContext>(
                raw => new ResultServerContext(raw, Bonus: 9))
            .Where((ctx, _) => ctx.Relation == CombatRelation.Pve)
            .RegisterLocal((ctx, serverContext, ct) =>
            {
                observed = ct;
                observedBonus = serverContext.Bonus;
                return new ValueTask<CombatDamageResult>(new CombatDamageResult
                {
                    Success = true,
                    Damage = ctx.Damage + serverContext.Bonus
                });
            }, priority: 12);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 21),
            dispatch.Token);

        Assert.True(result!.Value.Success);
        Assert.Equal(30, result.Value.Damage);
        Assert.Equal(9, observedBonus);
        Assert.Equal(dispatch.Token, observed);
    }
}
