using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Authoring;

/// <summary>
/// Authors a remote <c>RunLocal</c> reaction. The <c>Where</c> filter and <c>Select</c> projection lower to
/// server-side verified IR (they always run on the server); only the projected <c>MonsterId</c> crosses the
/// IPC boundary, where the native <c>RunLocal</c> delegate runs in the plugin process. Authored here (not in
/// the test) because lowering requires the DotBoxD plugin analyzer, which runs on this project.
/// </summary>
public static class LocalReactions
{
    public static void ConfigureCalmReaction(GamePluginHookRegistry hooks, Action<string> onCalmedMonster)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(onCalmedMonster);

        hooks.On<MonsterAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal(monsterId => onCalmedMonster(monsterId));
    }

    public static void ConfigureServerContextReaction(
        GamePluginHookRegistry hooks,
        Action<string, bool> onCalmedMonster)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(onCalmedMonster);

        hooks.On<MonsterAggroEvent>()
            .Where((e, _) => e.Distance <= 4)
            .Select((e, _) => e.MonsterId)
            .RunLocal((monsterId, context) =>
                onCalmedMonster(context.FormatCalmTarget(monsterId), context.HasCancelableDispatch));
    }

    /// <summary>
    /// Authors a remote whole-event <c>RunLocal</c> reaction (no <c>Select</c>). The <c>Where</c> filter lowers
    /// to server-side verified IR; for each matching event the WHOLE event record crosses the IPC boundary and
    /// the native delegate runs in the plugin process with the full event.
    /// </summary>
    public static void ConfigureWholeEventReaction(GamePluginHookRegistry hooks, Action<MonsterAggroEvent> onAggro)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(onAggro);

        hooks.On<MonsterAggroEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal(aggro => onAggro(aggro));
    }

    public static void ConfigureDamageDecision(GamePluginHookRegistry hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);

        hooks.On<RemoteDamageDecisionEvent>()
            .Where((e, _) => e.Damage > 10)
            .RegisterLocal(
                (e, context) => new RemoteDamageDecisionResult(
                    true,
                    context.DamageDecisionReason,
                    context.ScaleDamageDecision(e.Damage)),
                priority: 7);
    }
}
