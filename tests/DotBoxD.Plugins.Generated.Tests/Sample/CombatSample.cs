using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Generated.Tests.Sample;

// A small, self-contained generic game-server combat sample exercising result-returning hooks end to end:
// the host DamageSystem fires CombatDamageContext before committing HP and CombatDeathContext before a fatal
// commit, and applies whatever result the highest-priority matching plugin returns. Combatant traits the plugins
// filter on (boss, equipped item, buff) are carried as context fields the host populates, so the sample needs no
// host-handle infrastructure. The plugin registrations live in CombatSampleTests.

/// <summary>Broad combat categories; a single context covers all of them and filters select the subset.</summary>
public enum CombatRelation
{
    Pve = 0,
    Pvp = 1,
    Duel = 2,
    Environment = 3,
    Scripted = 4,
}

[Hook("combat.damage", typeof(CombatDamageResult))]
public sealed record CombatDamageContext(
    Combatant Attacker,
    Combatant Victim,
    CombatRelation Relation,
    int Damage,
    bool AttackerHasDivineSword,
    bool VictimIsBoss,
    int VictimHp);

[HookResult]
public readonly partial record struct CombatDamageResult(bool Success, string? Reason, int Damage);

[Hook("combat.death", typeof(CombatDeathResult))]
public sealed record CombatDeathContext(
    CombatRelation Relation,
    bool VictimIsPlayer,
    bool VictimHasCheatDeathBuff,
    int FatalDamage);

// PreventDeath defaults to false (the neutral/zero value), so a plain Ok() does not veto; a successful result
// that sets PreventDeath = true vetoes the death. Abstain (Success = false) falls through to the next handler.
[HookResult]
public readonly partial record struct CombatDeathResult(bool Success, string? Reason, bool PreventDeath);

[PolymorphicHandle(nameof(Id))]
[HandleSubtype(typeof(PlayerCombatant), "player", "combatant.player", "combatant.player.read")]
[HandleSubtype(typeof(MonsterCombatant), "monster", "combatant.monster", "combatant.monster.read")]
public abstract record Combatant(long Id);

public sealed record PlayerCombatant(long Id) : Combatant(Id)
{
    [HostBinding(
        "combatant.player.hasEquippedItem",
        "combatant.player.read",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public bool HasEquippedItem(long itemRuntimeId)
        => throw new NotSupportedException("Combatant host methods are lowering markers and are not called directly.");
}

public sealed record MonsterCombatant(long Id) : Combatant(Id)
{
    [HostBinding(
        "combatant.monster.isBoss",
        "combatant.monster.read",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public bool IsBoss()
        => throw new NotSupportedException("Combatant host methods are lowering markers and are not called directly.");
}

/// <summary>A mutable combat entity. Traits drive the context fields the host populates before firing.</summary>
public sealed class GameEntity
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public int Hp { get; set; }

    public bool IsPlayer { get; init; }

    public bool IsBoss { get; init; }

    public bool HasDivineSword { get; init; }

    public bool HasCheatDeathBuff { get; init; }
}

/// <summary>The result of applying one damage event: the committed damage and how the entity fared.</summary>
public readonly record struct DamageOutcome(int Damage, bool Died, bool DeathPrevented);

/// <summary>
/// Host-side damage pipeline. It fires the combat hooks, lets a verified (or local) plugin participate in the
/// decision, and applies the returned result to live HP — DotBoxD decides which handler wins and returns the
/// typed result; the host decides how to apply it.
/// </summary>
public sealed class DamageSystem
{
    private readonly PluginServer _server;

    public DamageSystem(PluginServer server) => _server = server;

    public async Task<DamageOutcome> ApplyDamageAsync(
        GameEntity attacker,
        GameEntity victim,
        int amount,
        CombatRelation relation,
        CancellationToken cancellationToken = default)
    {
        var damageContext = new CombatDamageContext(
            ToCombatant(attacker),
            ToCombatant(victim),
            relation,
            amount,
            attacker.HasDivineSword,
            victim.IsBoss,
            victim.Hp);
        var damageResult = await _server.Hooks
            .FireAsync<CombatDamageContext, CombatDamageResult>(damageContext, cancellationToken)
            .ConfigureAwait(false);
        var finalDamage = damageResult is { Success: true } applied ? applied.Damage : amount;
        if (finalDamage < 0)
        {
            finalDamage = 0;
        }

        if (victim.Hp - finalDamage <= 0)
        {
            var deathContext = new CombatDeathContext(
                relation, victim.IsPlayer, victim.HasCheatDeathBuff, finalDamage);
            var deathResult = await _server.Hooks
                .FireAsync<CombatDeathContext, CombatDeathResult>(deathContext, cancellationToken)
                .ConfigureAwait(false);
            if (deathResult is { Success: true, PreventDeath: true })
            {
                victim.Hp = 1;
                return new DamageOutcome(finalDamage, Died: false, DeathPrevented: true);
            }
        }

        victim.Hp = Math.Max(0, victim.Hp - finalDamage);
        return new DamageOutcome(finalDamage, Died: victim.Hp == 0, DeathPrevented: false);
    }

    private static Combatant ToCombatant(GameEntity entity)
        => entity.IsPlayer ? new PlayerCombatant(entity.Id) : new MonsterCombatant(entity.Id);
}
