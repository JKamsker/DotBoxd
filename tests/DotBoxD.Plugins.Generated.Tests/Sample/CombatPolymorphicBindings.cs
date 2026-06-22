using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Generated.Tests.Sample;

internal sealed class CombatPolymorphicBindings
{
    public const long DivineSwordItemRuntimeId = 9001L;

    private const SandboxEffect ReadEffects = SandboxEffect.Cpu | SandboxEffect.HostStateRead;
    private readonly Dictionary<long, GameEntity> _entities = [];

    public void Track(params GameEntity[] entities)
    {
        foreach (var entity in entities)
        {
            _entities[entity.Id] = entity;
        }
    }

    public void AddBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(BoolBinding("combatant.player.is", "combatant.player.read", (entity, _) => entity.IsPlayer));
        builder.AddBinding(BoolBinding(
            "combatant.player.hasEquippedItem",
            "combatant.player.read",
            (entity, args) =>
                entity.IsPlayer &&
                entity.HasDivineSword &&
                ((I64Value)args[1]).Value == DivineSwordItemRuntimeId,
            [SandboxType.I64, SandboxType.I64]));
        builder.AddBinding(BoolBinding("combatant.monster.is", "combatant.monster.read", (entity, _) => !entity.IsPlayer));
        builder.AddBinding(BoolBinding(
            "combatant.monster.isBoss",
            "combatant.monster.read",
            (entity, _) => !entity.IsPlayer && entity.IsBoss));
    }

    public static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("combatant.player.read", new { }, SandboxEffect.HostStateRead)
            .Grant("combatant.monster.read", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private BindingDescriptor BoolBinding(
        string id,
        string capability,
        Func<GameEntity, IReadOnlyList<SandboxValue>, bool> predicate,
        IReadOnlyList<SandboxType>? parameters = null)
        => new(
            id,
            SemVersion.One,
            parameters ?? [SandboxType.I64],
            SandboxType.Bool,
            ReadEffects,
            capability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                WriteAudit(context, id, capability, args);
                var idValue = ((I64Value)args[0]).Value;
                var matched = _entities.TryGetValue(idValue, out var entity) && predicate(entity, args);
                return ValueTask.FromResult(SandboxValue.FromBool(matched));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static void WriteAudit(
        SandboxContext context,
        string bindingId,
        string capability,
        IReadOnlyList<SandboxValue> args)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var entityId = ((I64Value)args[0]).Value;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: bindingId,
            CapabilityId: capability,
            Effect: ReadEffects,
            ResourceId: $"combatant:{entityId}",
            Fields: context.BindingAuditFields("combatant", startedAt)));
    }
}
