using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Game.Server.Simulation;

/// <summary>
/// Server-side backing for the gated <see cref="IGameWorldAccess"/> surface. Holds the live world
/// (bound after it is built, like <see cref="GameCommandSink"/>) and registers the host bindings a
/// kernel reaches through <c>ctx.Host&lt;IGameWorldAccess&gt;()</c>. Each binding is capability-gated, so a
/// kernel only runs reads or writes when the install policy grants the capability. The guardian's
/// <c>game.world.monster.read.*</c> grant covers health reads but not getThreat's separate
/// <c>game.world.combat.threat</c> capability.
/// </summary>
internal sealed class GameWorldHost
{
    private GameWorld? _world;

    /// <summary>Bound after the world is built (the world needs the hooks, the bindings need the world).</summary>
    public void Bind(GameWorld world) => _world = world;

    public void AddBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(SnapshotBinding("host.world.getMonster", "game.world.monster.read.snapshot", Snapshot));
        builder.AddBinding(IntReadBinding("host.world.getHealth", "game.world.monster.read.health", Health));
        builder.AddBinding(BoolBinding(
            "host.world.isMonster",
            "game.world.monster.read.kind",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            BindingSafety.ReadOnlyExternal,
            IsMonster));
        builder.AddBinding(IntReadBinding("host.world.getLevel", "game.world.monster.read.level", Level));
        builder.AddBinding(IntReadBinding("host.world.getPosition", "game.world.monster.read.position", Position));
        builder.AddBinding(BoolBinding(
            "host.world.killMonster",
            "game.world.monster.write.kill",
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite,
            BindingSafety.SideEffectingExternal,
            KillMonster));
        builder.AddBinding(ReadBinding("host.world.getThreat", "game.world.combat.threat", Threat));
    }

    private int Health(string entityId) => _world?.GetHealth(entityId) ?? 0;

    private MonsterSnapshot Snapshot(string entityId)
        => _world?.GetMonsterSnapshot(entityId) ?? new MonsterSnapshot(entityId, string.Empty, 0, 0, 0);

    private bool IsMonster(string entityId) => _world?.IsMonster(entityId) ?? false;

    private int Level(string entityId) => _world?.GetLevel(entityId) ?? 0;

    private int Position(string entityId) => _world?.GetPosition(entityId) ?? 0;

    private bool KillMonster(string entityId) => _world?.KillMonster(entityId) ?? false;

    private int Threat(string entityId)
        => _world?.FindEntity(entityId) is { } entity ? Math.Max(0, entity.Level) : 0;

    private static BindingDescriptor IntReadBinding(string id, string capability, Func<string, int> read)
        => IntBinding(id, capability, SandboxEffect.Cpu | SandboxEffect.HostStateRead, BindingSafety.ReadOnlyExternal, read);

    private static BindingDescriptor SnapshotBinding(string id, string capability, Func<string, MonsterSnapshot> read)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SnapshotType(),
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead,
            capability,
            BindingCostModel.Fixed(3),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var entityId = ((StringValue)args[0]).Value;
                var value = read(entityId);
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: id,
                    CapabilityId: capability,
                    Effect: AuditEffect(SandboxEffect.HostStateRead),
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("game-world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromRecord(
                [
                    SandboxValue.FromString(value.Id),
                    SandboxValue.FromString(value.Name),
                    SandboxValue.FromInt32(value.Health),
                    SandboxValue.FromInt32(value.Level),
                    SandboxValue.FromInt32(value.Position)
                ]));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static SandboxType SnapshotType()
        => SandboxType.Record(
        [
            SandboxType.String,
            SandboxType.String,
            SandboxType.I32,
            SandboxType.I32,
            SandboxType.I32
        ]);

    private static BindingDescriptor ReadBinding(string id, string capability, Func<string, int> read)
        => IntReadBinding(id, capability, read);

    private static BindingDescriptor IntBinding(
        string id,
        string capability,
        SandboxEffect effect,
        BindingSafety safety,
        Func<string, int> read)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.I32,
            effect,
            capability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            safety,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var entityId = ((StringValue)args[0]).Value;
                var value = read(entityId);
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: id,
                    CapabilityId: capability,
                    Effect: AuditEffect(effect),
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("game-world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            // DotBoxD.Kernels.Runtime is referenced transitively (via DotBoxD.Plugins); the stub is metadata for
            // the compiled path, which this example does not enable, so it is never invoked.
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            // Custom capabilities require a grant validator; these reads accept any grant shape.
            GrantValidator: static (_, _) => { });

    private static BindingDescriptor BoolBinding(
        string id,
        string capability,
        SandboxEffect effect,
        BindingSafety safety,
        Func<string, bool> read)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.Bool,
            effect,
            capability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            safety,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var entityId = ((StringValue)args[0]).Value;
                var value = read(entityId);
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: id,
                    CapabilityId: capability,
                    Effect: AuditEffect(effect),
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("game-world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(value));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static SandboxEffect AuditEffect(SandboxEffect effect)
        => effect & (SandboxEffect.HostStateRead | SandboxEffect.HostStateWrite);
}
