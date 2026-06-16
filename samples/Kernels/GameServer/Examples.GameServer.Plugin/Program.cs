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
///   <item><c>Replace&lt;TService, TKernel&gt;()</c> — install an <c>[EventKernel]</c> that swaps a whole
///   event service (root verb).</item>
///   <item><c>Monsters.Extend&lt;TKernel&gt;()</c> — install a <c>[ServerExtension]</c>; grafts a method onto
///   the control (batch) or onto each <c>IMonster</c> handle (per-instance).</item>
///   <item><c>Monsters.Get(id)</c> — a scoped handle; calls on it omit the id.</item>
///   <item><c>Get&lt;TKernel&gt;()</c> — tune an installed kernel's live settings.</item>
///   <item><c>InvokeAsync(...)</c> — a throwaway server-side probe (see <c>AdvancedUsage</c>).</item>
/// </list>
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string pipeName;
        try
        {
            pipeName = GamePluginServerHost.PipeNameFromArgs(args);   // throws ArgumentException on misuse
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }

        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");

        using var server = GamePluginServerBuilder.FromPipeName(pipeName).Build();   // sync, no I/O
        await server.StartAsync();

        // Install plugin-owned kernels — ships verified IR; install ids derive from the kernel type.
        //   Replace<> installs an [EventKernel] sync reaction (swaps a whole event service).
        //   Extend<>  grafts a [ServerExtension] async kernel onto a control (batch) or an IMonster handle.
        await server.Replace<IMonsterAggroService, GuardianKernel>();
        await server.Replace<IAttackService, RetaliationKernel>();
        await server.Monsters.Extend<MonsterKillerKernel>();   // grafts onto IMonsterControl (batch)
        await server.Monsters.Extend<BlinkKernel>();            // grafts onto IMonster handles (per-instance)

        // One direct domain call via a scoped handle — the id is captured by Get(id), so KillAsync omits it.
        var killed = await server.Monsters.Get("monster-4").KillAsync();
        Console.WriteLine($"[plugin] Monsters.Get(monster-4).KillAsync() => {killed}.");

        // Tune a replaced kernel's live settings — strongly typed member setters, one atomic batch. Only
        // [LiveSetting] members are settable; ApplyAsync ships it (a chain without ApplyAsync warns).
        await server.Get<GuardianKernel>()
            .Set(k => k.CalmStrength, 35)
            .Set(k => k.AggroRange, 6)
            .ApplyAsync(atomic: true);

        // The advanced surface (handles, server-extension calls, InvokeAsync probes) lives in its own file.
        await AdvancedUsage.RunAsync(server);

        Console.WriteLine("[plugin] kernels live; holding until server completes...");
        await server.HoldUntilShutdownAsync();
        return 0;
    }
}
