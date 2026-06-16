using DotBoxD.Kernels.Game.Plugin.Kernels;

namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// Advanced server-side reads, kept out of the golden Program.cs so the first-run path stays short.
///
/// <para><c>InvokeAsync</c> ships a lambda as verified IR that runs sandboxed on the server, reading the same
/// async world surface (local there). Two overloads:</para>
/// <list type="bullet">
///   <item><b>plain / explicit capture-bag (recommended)</b> — values in and out are marshaled explicitly
///   across the sandbox boundary; the bag's assigned fields are written back after the await.</item>
///   <item><b>implicit local capture (advanced)</b> — closes over outer locals and writes them back;
///   convenient, but prefer the capture bag when you need a value back.</item>
/// </list>
/// </summary>
internal static class AdvancedUsage
{
    public static async Task RunAsync(GamePluginServer server)
    {
        // Generated server-extension graft (MonsterKillerKernel grafted onto IMonsterControl). Called exactly
        // like a native control method.
        var killResults = await server.Monsters.KillMonstersAsync(["monster-3", "monster-4", "player-1"]);
        Console.WriteLine($"[plugin] Monsters.KillMonstersAsync(...) => {killResults.Count} results.");

        var health = await server.Entities.GetHealthAsync("monster-2");
        Console.WriteLine($"[plugin] Entities.GetHealthAsync(monster-2) => {health}.");

        // Plain probe — read-only, returns a value. The lambda is lowered to verified IR and runs sandboxed.
        var monsterHealth = await server.InvokeAsync(async (IGameWorldAccess world) =>
        {
            var monster = await world.Monsters.GetAsync("monster-2");
            return monster.Health;
        });
        Console.WriteLine($"[plugin] InvokeAsync GetAsync(monster-2).Health => {monsterHealth}.");

        // RECOMMENDED when you need values back across the boundary: an explicit capture bag. Sync in, sync
        // out — the assigned fields are written back onto `probe` after the await.
        var probe = new MonsterProbeCapture { MonsterId = "monster-2" };
        var monsterName = await server.InvokeAsync(probe, async (IGameWorldAccess world, MonsterProbeCapture bag) =>
        {
            var monster = await world.Monsters.GetAsync(bag.MonsterId);
            bag.LastHealth = monster.Health;
            return monster.Name;
        });
        Console.WriteLine($"[plugin] InvokeAsync capture {probe.MonsterId} => {monsterName} hp={probe.LastHealth}.");

        // ADVANCED — implicit capture closes over a local and writes it back. Convenient, but the closure
        // shape must be provable; prefer the capture bag above when you need write-back.
        var lastHealth = 0;
        var name = await server.InvokeAsync(async (IGameWorldAccess world) =>
        {
            var monster = await world.Monsters.GetAsync("monster-2");
            lastHealth = monster.Health;
            return monster.Name;
        });
        Console.WriteLine($"[plugin] InvokeAsync implicit capture => {name} hp={lastHealth}.");
    }
}

/// <summary>Plain dev-authored capture object for the explicit capture-bag <c>InvokeAsync</c> overload.</summary>
public sealed class MonsterProbeCapture
{
    public string MonsterId { get; set; } = string.Empty;
    public int LastHealth { get; set; }
}
