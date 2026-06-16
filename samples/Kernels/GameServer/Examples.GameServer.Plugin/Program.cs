using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Kernels.Game.Plugin.Kernels;

namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// The golden path, from the plugin dev's seat. The server implements <c>IGameWorldAccess</c>; the plugin
/// holds a generated RPC proxy of the same interface (that proxy IS <c>server</c>); kernels get it injected.
/// One surface, three call sites.
///
/// <para><b>Which verb when:</b></para>
/// <list type="bullet">
///   <item><c>Replace&lt;TEvent, TKernel&gt;()</c> — swap a whole event service (root verb).</item>
///   <item><c>Monsters.Extend&lt;TKernel&gt;()</c> — graft a reusable named batch method onto a control.</item>
///   <item><c>Monsters.KillAsync(...)</c> — a direct domain RPC.</item>
///   <item><c>Get&lt;TKernel&gt;()</c> — tune an installed kernel's live settings.</item>
///   <item><c>InvokeAsync(...)</c> — a throwaway server-side probe (see <c>AdvancedUsage</c>).</item>
/// </list>
/// Advanced reads (server extensions, InvokeAsync probes, capture bags) live in AdvancedUsage.cs.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pipeName = GamePluginServerHost.PipeNameFromArgs(args);   // parse + usage in one line
        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");

        using var server = GamePluginServerBuilder.FromPipeName(pipeName).Build();   // sync, no I/O
        await server.StartAsync();

        // Install plugin-owned kernels — ships verified IR. Install ids derive from the kernel type, so the
        // verbs are keyed purely by type: nothing to keep in sync.
        await server.Replace<IMonsterAggroService, GuardianKernel>();
        await server.Replace<IAttackService, RetaliationKernel>();
        await server.Monsters.Extend<MonsterKillerKernel>();

        // One direct domain call — the same IGameWorldAccess.Monsters.KillAsync the server implements and the
        // kernels call. No wire contract, no separate name.
        var killed = await server.Monsters.KillAsync("monster-4");
        Console.WriteLine($"[plugin] Monsters.KillAsync(monster-4) => {killed}.");

        // Tune a replaced kernel's live settings — strongly typed member setters, one atomic batch. Only
        // [LiveSetting] members are settable; you cannot read or mutate the kernel here.
        await server.Get<GuardianKernel>()
            .Set(k => k.CalmStrength, 35)
            .Set(k => k.AggroRange, 6)
            .ApplyAsync(atomic: true);

        // The advanced surface (server-extension calls + InvokeAsync probes) lives in its own file.
        await AdvancedUsage.RunAsync(server);

        Console.WriteLine("[plugin] kernels live; holding until server completes...");
        await server.HoldUntilShutdownAsync();
        return 0;
    }
}
