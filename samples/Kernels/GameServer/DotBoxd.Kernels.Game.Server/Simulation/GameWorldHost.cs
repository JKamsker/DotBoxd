namespace DotBoxd.Kernels.Game.Server;

using DotBoxd.Hosting;

/// <summary>
/// Server-side backing for the gated <see cref="IGameWorldAccess"/> surface. Holds the live world
/// (bound after it is built, like <see cref="GameCommandSink"/>) and registers the two read bindings a
/// kernel reaches through <c>ctx.Host&lt;IGameWorldAccess&gt;()</c>: <c>host.world.getHealth</c> and
/// <c>host.world.getThreat</c>. Each is a capability-gated host-state read, so a kernel only runs it
/// when the policy grants the capability — the guardian's <c>game.world.monster.read.*</c> grant covers
/// getHealth but not getThreat's <c>game.world.combat.threat</c>.
/// </summary>
internal sealed class GameWorldHost
{
    private GameWorld? _world;

    /// <summary>Bound after the world is built (the world needs the hooks, the bindings need the world).</summary>
    public void Bind(GameWorld world) => _world = world;

    public void AddBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(ReadBinding("host.world.getHealth", "game.world.monster.read.health", Health));
        builder.AddBinding(ReadBinding("host.world.getThreat", "game.world.combat.threat", Threat));
    }

    private int Health(string entityId) => _world?.FindEntity(entityId)?.Hp ?? 0;

    private int Threat(string entityId)
        => _world?.FindEntity(entityId) is { } entity ? Math.Max(0, entity.Level) : 0;

    private static BindingDescriptor ReadBinding(string id, string capability, Func<string, int> read)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            capability,
            BindingCostModel.Fixed(2),
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
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("game-world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            // DotBoxd.Kernels.Runtime is referenced transitively (via DotBoxd.Plugins); the stub is metadata for
            // the compiled path, which this example does not enable, so it is never invoked.
            CompiledBinding.RuntimeStub("DotBoxd.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            // Custom capabilities require a grant validator; these reads accept any grant shape.
            GrantValidator: static (_, _) => { });
}
