namespace DotBoxD.Plugins.Generated.Tests.Sample;

/// <summary>
/// Sample plugins for the combat DamageSystem, authored as ordinary result-hook chains the DotBoxD generator
/// intercepts at build time, plus behavioural assertions. Each registration is a distinct lowered chain:
/// <list type="bullet">
///   <item><b>Divine Sword</b>: a player with the item doubles PvE damage (verified Register).</item>
///   <item><b>Boss Shield</b>: boss victims take half damage (verified Register, lower priority).</item>
///   <item><b>Duel Rule</b>: duel damage is capped to leave 1 HP — expressed with Math.Min, so it lowers only
///   the Relation==Duel filter and produces the result in-process (RegisterLocal).</item>
///   <item><b>Cheat Death</b>: a buffed player vetoes a fatal hit (verified Register on the death hook).</item>
/// </list>
/// </summary>
public sealed class CombatSampleTests
{
    private static void RegisterDivineSword(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Where(ctx => ctx.AttackerHasDivineSword)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 100);

    private static void RegisterBossShield(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.VictimIsBoss)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage / 2), priority: 50);

    private static void RegisterDuelRule(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Duel)
            .RegisterLocal(
                (ctx, _) => CombatDamageResult.Ok().WithDamage(Math.Min(ctx.Damage, ctx.VictimHp - 1)),
                priority: 10);

    private static void RegisterCheatDeath(PluginServer server)
        => server.Hooks.On<CombatDeathContext>()
            .Where(ctx => ctx.VictimIsPlayer)
            .Where(ctx => ctx.VictimHasCheatDeathBuff)
            .Register(ctx => CombatDeathResult.Ok().WithPreventDeath(true), priority: 100);

    private static GameEntity Player(int hp, bool sword = false, bool cheatDeath = false)
        => new() { Name = "hero", Hp = hp, IsPlayer = true, HasDivineSword = sword, HasCheatDeathBuff = cheatDeath };

    private static GameEntity Monster(int hp, bool boss = false)
        => new() { Name = "monster", Hp = hp, IsBoss = boss };

    [Fact]
    public async Task DivineSword_doubles_player_pve_damage_to_monsters()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterDivineSword(server);
        var system = new DamageSystem(server);
        var monster = Monster(hp: 500);

        var outcome = await system.ApplyDamageAsync(Player(100, sword: true), monster, amount: 50, CombatRelation.Pve);

        Assert.Equal(100, outcome.Damage);
        Assert.Equal(400, monster.Hp);
    }

    [Fact]
    public async Task DivineSword_does_not_apply_to_pvp_or_without_the_item()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterDivineSword(server);
        var system = new DamageSystem(server);

        var pvp = await system.ApplyDamageAsync(Player(100, sword: true), Player(500), amount: 50, CombatRelation.Pvp);
        Assert.Equal(50, pvp.Damage);

        var noItem = await system.ApplyDamageAsync(Player(100), Monster(500), amount: 50, CombatRelation.Pve);
        Assert.Equal(50, noItem.Damage);
    }

    [Fact]
    public async Task BossShield_halves_incoming_damage_to_bosses()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterBossShield(server);
        var system = new DamageSystem(server);
        var boss = Monster(hp: 500, boss: true);

        var outcome = await system.ApplyDamageAsync(Monster(10), boss, amount: 80, CombatRelation.Environment);

        Assert.Equal(40, outcome.Damage);
    }

    [Fact]
    public async Task Higher_priority_DivineSword_wins_over_BossShield_first_success_only()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterDivineSword(server);
        RegisterBossShield(server);
        var system = new DamageSystem(server);
        var boss = Monster(hp: 500, boss: true);

        // Divine Sword (priority 100) succeeds first, so its doubling wins; Boss Shield's halving never runs.
        var outcome = await system.ApplyDamageAsync(Player(100, sword: true), boss, amount: 50, CombatRelation.Pve);

        Assert.Equal(100, outcome.Damage);
    }

    [Fact]
    public async Task DuelRule_caps_damage_to_leave_one_hp_via_register_local()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterDuelRule(server);
        var system = new DamageSystem(server);
        var defender = Player(hp: 30);

        var outcome = await system.ApplyDamageAsync(Player(100), defender, amount: 100, CombatRelation.Duel);

        Assert.Equal(29, outcome.Damage);
        Assert.Equal(1, defender.Hp);
        Assert.False(outcome.Died);
    }

    [Fact]
    public async Task DuelRule_leaves_smaller_hits_unchanged()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterDuelRule(server);
        var system = new DamageSystem(server);
        var defender = Player(hp: 30);

        var outcome = await system.ApplyDamageAsync(Player(100), defender, amount: 10, CombatRelation.Duel);

        Assert.Equal(10, outcome.Damage);
        Assert.Equal(20, defender.Hp);
    }

    [Fact]
    public async Task CheatDeath_prevents_the_fatal_hit_for_a_buffed_player()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterCheatDeath(server);
        var system = new DamageSystem(server);
        var player = Player(hp: 20, cheatDeath: true);

        var outcome = await system.ApplyDamageAsync(Monster(10), player, amount: 100, CombatRelation.Pve);

        Assert.True(outcome.DeathPrevented);
        Assert.False(outcome.Died);
        Assert.Equal(1, player.Hp);
    }

    [Fact]
    public async Task CheatDeath_does_not_save_an_unbuffed_player()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterCheatDeath(server);
        var system = new DamageSystem(server);
        var player = Player(hp: 20);

        var outcome = await system.ApplyDamageAsync(Monster(10), player, amount: 100, CombatRelation.Pve);

        Assert.True(outcome.Died);
        Assert.Equal(0, player.Hp);
    }

    [Fact]
    public async Task CheatDeath_does_not_prevent_non_fatal_damage()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterCheatDeath(server);
        var system = new DamageSystem(server);
        var player = Player(hp: 200, cheatDeath: true);

        var outcome = await system.ApplyDamageAsync(Monster(10), player, amount: 50, CombatRelation.Pve);

        Assert.False(outcome.DeathPrevented);
        Assert.Equal(150, player.Hp);
    }
}
