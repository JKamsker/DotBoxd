using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// Advanced surface, kept out of the golden Program.cs so the first-run path stays short: entity handles,
/// server-extension calls, local hook terminals, and <c>InvokeAsync</c> probes.
///
/// <para><c>InvokeAsync</c> ships a lambda as verified IR that runs sandboxed on the server, reading the same
/// async world surface (local there). <b>Two overloads, two call shapes:</b></para>
/// <list type="bullet">
///   <item><b>plain single-lambda</b> — read-only probe, a value out.</item>
///   <item><b>explicit capture-bag (recommended for write-back)</b> — the second overload; the bag's assigned
///   fields are written back after the await.</item>
/// </list>
/// </summary>
internal static class AdvancedUsage
{
    public static async Task RunAsync(IGameWorldServer server)
    {
        // ── Generated server-extension graft (MonsterKillerKernel grafted onto the IMonsterControl collection).
        var killResults = await server.Monsters.KillMonstersAsync(["monster-3", "monster-4", "player-1"]);
        Console.WriteLine($"[plugin] Monsters.KillMonstersAsync(...) => {killResults.Count} results.");

        // ── Entity HANDLE — Get(id) captures the id once; every call on the handle omits it.
        var monster = server.Monsters.Get("monster-2");
        var hp = await monster.GetHealthAsync();
        var threat = await monster.GetThreatAsync();
        Console.WriteLine($"[plugin] Monsters.Get(monster-2): hp={hp} threat={threat}.");

        // Built-in instance write — no id re-specified.
        await server.Monsters.Get("monster-4").TeleportToAsync(3);

        // Instance-scoped server extension (BlinkKernel grafted onto IMonster). The kernel gets THIS monster
        // injected (plus the root world); the call omits the monster id entirely.
        var landed = await server.Monsters.Get("monster-4").BlinkBehindAsync("player-1");
        Console.WriteLine($"[plugin] Monsters.Get(monster-4).BlinkBehindAsync(player-1) => landed at {landed}.");

        // ── InvokeAsync (a) plain probe — read-only, returns a value.
        var monsterHealth = await server.InvokeAsync(async (IGameWorldAccess world) =>
        {
            var monster = world.Monsters.Get("monster-2");
            var snapshot = await monster.SnapshotAsync();
            return snapshot.Health;
        });
        Console.WriteLine($"[plugin] InvokeAsync Get(monster-2).Snapshot.Health => {monsterHealth}.");

        // ── InvokeAsync (b) explicit capture-bag — RECOMMENDED when you need values back across the boundary.
        // Sync in, sync out: the assigned fields are written back onto `probe` after the await.
        var probe = new MonsterProbeCapture { MonsterId = "monster-2" };
        var monsterName = await server.InvokeAsync(probe, async (IGameWorldAccess world, MonsterProbeCapture bag) =>
        {
            var monster = world.Monsters.Get(bag.MonsterId);
            var snapshot = await monster.SnapshotAsync();
            bag.LastHealth = snapshot.Health;
            return snapshot.Name;
        });
        Console.WriteLine($"[plugin] InvokeAsync capture {probe.MonsterId} => {monsterName} hp={probe.LastHealth}.");

        await RunLocalHookPreviewAsync();
    }

    private static async Task RunLocalHookPreviewAsync()
    {
        var observed = new List<string>();
        using var local = PluginServer.Create();
        local.Hooks.On<AttackEvent>()
            .Where(e => e.Damage >= 10)
            .Select(e => e.AttackerId)
            .RunLocal(attackerId => observed.Add(attackerId));

        await local.Hooks.PublishAsync(new AttackEvent("monster-local", "player-local", 12, 7));
        Console.WriteLine($"[plugin] local hook RunLocal observed {observed.Count} attack event.");
    }
}

/// <summary>Plain dev-authored capture object for the explicit capture-bag <c>InvokeAsync</c> overload.</summary>
public sealed class MonsterProbeCapture
{
    public string MonsterId { get; set; } = string.Empty;
    public int LastHealth { get; set; }
}
