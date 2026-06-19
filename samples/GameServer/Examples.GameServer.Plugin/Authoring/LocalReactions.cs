using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Game.Plugin.Authoring;

/// <summary>
/// Authors a remote <c>RunLocal</c> reaction. The <c>Where</c> filter and <c>Select</c> projection lower to
/// server-side verified IR (they always run on the server); only the projected <c>MonsterId</c> crosses the
/// IPC boundary, where the native <c>RunLocal</c> delegate runs in the plugin process. Authored here (not in
/// the test) because lowering requires the DotBoxD plugin analyzer, which runs on this project.
/// </summary>
public static class LocalReactions
{
    public static void ConfigureCalmReaction(RemoteHookRegistry hooks, Action<string> onCalmedMonster)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(onCalmedMonster);

        hooks.On<MonsterAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.MonsterId)
            .RunLocal(monsterId => onCalmedMonster(monsterId));
    }

    /// <summary>
    /// Authors a remote whole-event <c>RunLocal</c> reaction (no <c>Select</c>). The <c>Where</c> filter lowers
    /// to server-side verified IR; for each matching event the WHOLE event record crosses the IPC boundary and
    /// the native delegate runs in the plugin process with the full event.
    /// </summary>
    public static void ConfigureWholeEventReaction(RemoteHookRegistry hooks, Action<MonsterAggroEvent> onAggro)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(onAggro);

        hooks.On<MonsterAggroEvent>()
            .Where(e => e.Distance <= 4)
            .RunLocal(aggro => onAggro(aggro));
    }
}
