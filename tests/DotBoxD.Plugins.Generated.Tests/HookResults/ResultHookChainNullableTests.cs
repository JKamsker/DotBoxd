namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public sealed partial class ResultHookChainTests
{
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_round_trips_present_nullable_bool_result_fields(bool canDie)
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<OptionalDamageContext>()
            .Register(
                ctx => OptionalDamageResult.Ok()
                    .WithDamage(ctx.Damage)
                    .WithCanDie(ctx.CanDie),
                priority: 10);

        var result = await server.Hooks.FireAsync<OptionalDamageContext, OptionalDamageResult>(
            new OptionalDamageContext(15, canDie));

        Assert.True(result!.Value.Success);
        Assert.Equal(15, result.Value.Damage);
        Assert.Equal(canDie, result.Value.CanDie);
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
}
