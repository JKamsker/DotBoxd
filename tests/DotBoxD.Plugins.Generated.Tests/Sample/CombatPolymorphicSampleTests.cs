namespace DotBoxD.Plugins.Generated.Tests.Sample;

public sealed class CombatPolymorphicSampleTests
{
    private static void RegisterContextFieldDivineSword(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Where(ctx => ctx.AttackerHasDivineSword)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 100);

    private static void RegisterPolymorphicDivineSword(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                          attacker.HasEquippedItem(CombatPolymorphicBindings.DivineSwordItemRuntimeId))
            .Where(ctx => ctx.Victim is MonsterCombatant)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 100);

    private static void RegisterContextFieldBossShield(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.VictimIsBoss)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage / 2), priority: 50);

    private static void RegisterPolymorphicBossShield(PluginServer server)
        => server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Victim is MonsterCombatant monster && monster.IsBoss())
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage / 2), priority: 50);

    private static GameEntity Player(long id, int hp, bool sword = false)
        => new() { Id = id, Name = "hero", Hp = hp, IsPlayer = true, HasDivineSword = sword };

    private static GameEntity Monster(long id, int hp, bool boss = false)
        => new() { Id = id, Name = "monster", Hp = hp, IsBoss = boss };

    [Fact]
    public async Task Polymorphic_DivineSword_matches_the_context_field_plugin()
    {
        using var fieldServer = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterContextFieldDivineSword(fieldServer);

        var bindings = new CombatPolymorphicBindings();
        using var polymorphicServer = PluginServer.Create(
            configureHost: bindings.AddBindings,
            defaultPolicy: CombatPolymorphicBindings.Policy());
        RegisterPolymorphicDivineSword(polymorphicServer);

        var fieldMonster = Monster(2, hp: 500);
        var polymorphicHero = Player(11, hp: 100, sword: true);
        var polymorphicMonster = Monster(12, hp: 500);
        bindings.Track(polymorphicHero, polymorphicMonster);

        var fieldOutcome = await new DamageSystem(fieldServer)
            .ApplyDamageAsync(Player(1, hp: 100, sword: true), fieldMonster, 50, CombatRelation.Pve);
        var polymorphicOutcome = await new DamageSystem(polymorphicServer)
            .ApplyDamageAsync(polymorphicHero, polymorphicMonster, 50, CombatRelation.Pve);

        Assert.Equal(fieldOutcome, polymorphicOutcome);
        Assert.Equal(fieldMonster.Hp, polymorphicMonster.Hp);
    }

    [Fact]
    public async Task Polymorphic_BossShield_matches_the_context_field_plugin()
    {
        using var fieldServer = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        RegisterContextFieldBossShield(fieldServer);

        var bindings = new CombatPolymorphicBindings();
        using var polymorphicServer = PluginServer.Create(
            configureHost: bindings.AddBindings,
            defaultPolicy: CombatPolymorphicBindings.Policy());
        RegisterPolymorphicBossShield(polymorphicServer);

        var fieldBoss = Monster(2, hp: 500, boss: true);
        var polymorphicAttacker = Monster(21, hp: 10);
        var polymorphicBoss = Monster(22, hp: 500, boss: true);
        bindings.Track(polymorphicAttacker, polymorphicBoss);

        var fieldOutcome = await new DamageSystem(fieldServer)
            .ApplyDamageAsync(Monster(1, hp: 10), fieldBoss, 80, CombatRelation.Environment);
        var polymorphicOutcome = await new DamageSystem(polymorphicServer)
            .ApplyDamageAsync(polymorphicAttacker, polymorphicBoss, 80, CombatRelation.Environment);

        Assert.Equal(fieldOutcome, polymorphicOutcome);
        Assert.Equal(fieldBoss.Hp, polymorphicBoss.Hp);
    }

    [Fact]
    public async Task Polymorphic_DivineSword_skips_monster_attackers()
    {
        var bindings = new CombatPolymorphicBindings();
        using var server = PluginServer.Create(
            configureHost: bindings.AddBindings,
            defaultPolicy: CombatPolymorphicBindings.Policy());
        RegisterPolymorphicDivineSword(server);

        var attacker = Monster(31, hp: 100);
        var victim = Monster(32, hp: 500);
        bindings.Track(attacker, victim);

        var outcome = await new DamageSystem(server)
            .ApplyDamageAsync(attacker, victim, 50, CombatRelation.Pve);

        Assert.Equal(50, outcome.Damage);
        Assert.Equal(450, victim.Hp);
    }

    [Fact]
    public async Task Polymorphic_DivineSword_skips_players_without_the_item()
    {
        var bindings = new CombatPolymorphicBindings();
        using var server = PluginServer.Create(
            configureHost: bindings.AddBindings,
            defaultPolicy: CombatPolymorphicBindings.Policy());
        RegisterPolymorphicDivineSword(server);

        var attacker = Player(41, hp: 100, sword: false);
        var victim = Monster(42, hp: 500);
        bindings.Track(attacker, victim);

        var outcome = await new DamageSystem(server)
            .ApplyDamageAsync(attacker, victim, 50, CombatRelation.Pve);

        Assert.Equal(50, outcome.Damage);
        Assert.Equal(450, victim.Hp);
    }

    [Fact]
    public async Task Polymorphic_or_filter_supports_right_branch_capture_and_left_branch_pass()
    {
        var bindings = new CombatPolymorphicBindings();
        using var server = PluginServer.Create(
            configureHost: bindings.AddBindings,
            defaultPolicy: CombatPolymorphicBindings.Policy());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Damage > 1000 ||
                          (ctx.Attacker is PlayerCombatant attacker &&
                           attacker.HasEquippedItem(CombatPolymorphicBindings.DivineSwordItemRuntimeId)))
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 100);

        var swordPlayer = Player(61, hp: 100, sword: true);
        var monsterAttacker = Monster(62, hp: 100);
        var victim = Monster(63, hp: 500);
        bindings.Track(swordPlayer, monsterAttacker, victim);

        var rightBranch = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(
                new PlayerCombatant(swordPlayer.Id),
                new MonsterCombatant(victim.Id),
                CombatRelation.Pve,
                Damage: 50,
                AttackerHasDivineSword: false,
                VictimIsBoss: false,
                VictimHp: victim.Hp));
        var leftBranch = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(
                new MonsterCombatant(monsterAttacker.Id),
                new MonsterCombatant(victim.Id),
                CombatRelation.Pve,
                Damage: 1001,
                AttackerHasDivineSword: false,
                VictimIsBoss: false,
                VictimHp: victim.Hp));

        Assert.Equal(100, rightBranch!.Value.Damage);
        Assert.Equal(2002, leftBranch!.Value.Damage);
    }

    [Fact]
    public async Task Polymorphic_BossShield_skips_non_boss_monsters()
    {
        var bindings = new CombatPolymorphicBindings();
        using var server = PluginServer.Create(
            configureHost: bindings.AddBindings,
            defaultPolicy: CombatPolymorphicBindings.Policy());
        RegisterPolymorphicBossShield(server);

        var attacker = Monster(51, hp: 100);
        var victim = Monster(52, hp: 500, boss: false);
        bindings.Track(attacker, victim);

        var outcome = await new DamageSystem(server)
            .ApplyDamageAsync(attacker, victim, 80, CombatRelation.Environment);

        Assert.Equal(80, outcome.Damage);
        Assert.Equal(420, victim.Hp);
    }
}
