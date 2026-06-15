using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Game.Plugin;

using DotBoxD.Kernels.Game.Plugin.Client;

/// <summary>
/// The plugin process. Connects to the game server's control plane, ships each kernel as verified IR
/// (the server never sees kernel source), tunes live settings over IPC, then exits so the server
/// proceeds to its with-plugin phase.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--use-builder"))
        {
            await Console.Error
                .WriteLineAsync("Usage: Examples.GameServer.Plugin <named-pipe-name> [--use-builder]")
                .ConfigureAwait(false);
            return 1;
        }

        var pipeName = args[0];
        return args.Length == 2
            ? await RunWithBuilderAsync(pipeName).ConfigureAwait(false)
            : await RunImperativeAsync(pipeName).ConfigureAwait(false);
    }

    private static async Task<int> RunImperativeAsync(string pipeName)
    {
        // Connect to the game server's control plane and wrap it in a server-shaped shim.
        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");
        await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName).ConfigureAwait(false);
        var server = new RemotePluginServer(connection.Get<IGamePluginControlService>());

        // Register each kernel as the implementation of a server service contract. Register resolves
        // the kernel's generated verified IR and ships it — no Export/InstallPluginAsync plumbing.
        var guardianId = await server.Kernels.Register<IMonsterAggroService, GuardianKernel>().ConfigureAwait(false);
        Console.WriteLine($"[plugin] registered kernel '{guardianId}'.");

        var retaliationId = await server.Kernels.Register<IAttackService, RetaliationKernel>().ConfigureAwait(false);
        Console.WriteLine($"[plugin] registered kernel '{retaliationId}'.");

        var killerId = await server.KernelRpc.Register<IMonsterKillerService, MonsterKillerKernel>().ConfigureAwait(false);
        Console.WriteLine($"[plugin] registered kernel RPC service '{killerId}'.");

        await RunPluginWorkAsync(server).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunWithBuilderAsync(string pipeName)
    {
        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");
        await using var server = RemotePluginServerBuilder
            .FromPipeName(pipeName)
            .SetupKernels(kernels => kernels
                .Register<IMonsterAggroService, GuardianKernel>()
                .Register<IAttackService, RetaliationKernel>())
            .SetupKernelRpc(kernelRpc => kernelRpc
                .Register<IMonsterKillerService, MonsterKillerKernel>())
            .Build();

        await server.StartAsync().ConfigureAwait(false);
        Console.WriteLine($"[plugin] registered kernel '{RemoteKernelControl.PluginId(typeof(GuardianKernel))}'.");
        Console.WriteLine($"[plugin] registered kernel '{RemoteKernelControl.PluginId(typeof(RetaliationKernel))}'.");
        Console.WriteLine($"[plugin] registered kernel RPC service '{server.KernelRpc.PluginId<IMonsterKillerService>()}'.");

        await RunPluginWorkAsync(server).ConfigureAwait(false);
        return 0;
    }

    private static async Task RunPluginWorkAsync(RemotePluginServer server)
    {
        // Tune live settings — strongly typed, one atomic IPC batch under the hood.
        await server.Kernels.Get<GuardianKernel>()
            .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true)
            .ConfigureAwait(false);
        Console.WriteLine("[plugin] tuned guardian live settings (CalmStrength=35, AggroRange=6).");

        // Ordinary IPC can expose direct server APIs too. This call goes straight to the server's
        // world service and kills one monster in one IPC call.
        var directKilled = await server.World.Monsters.KillAsync("monster-4").ConfigureAwait(false);
        Console.WriteLine($"[plugin] ordinary IPC KillMonster(monster-4) => {directKilled}.");

        // Kernel RPC keeps the plugin-owned loop on the server. The plugin sends one request, the
        // server executes the generated verified IR, and the result list comes back as plugin DTOs.
        // The generated property flavor is also available as server.World.Monsters.MonsterKiller.
        var killResults = await server.World.Monsters
            .KillMonstersAsync(["monster-3", "monster-4", "player-1", "monster-missing"])
            .ConfigureAwait(false);
        Console.WriteLine("[plugin] kernel RPC KillMonsters(...) => " + FormatKillResults(killResults));

        var monsterHealth = await server.Kernels.InvokeAsync((IGameWorldAccess world) =>
        {
            var monster = world.GetMonster("monster-2");
            return monster.Health;
        }).ConfigureAwait(false);
        Console.WriteLine($"[plugin] invoke async GetMonster(monster-2).Health => {monsterHealth}.");

        // Hold the connection open so the kernels stay owned and live while the server runs its
        // with-plugin phase. When the server signals shutdown this returns and we disconnect, at which
        // point the server unloads our kernels (ownership = connection lifetime).
        Console.WriteLine("[plugin] kernels live; holding connection until server completes...");
        await server.HoldUntilShutdownAsync().ConfigureAwait(false);

        Console.WriteLine("[plugin] released by server. Disconnecting (kernels will be unloaded). Exiting.");
    }

    private static string FormatKillResults(IReadOnlyList<MonsterKillResult> results)
    {
        var parts = new string[results.Count];
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            parts[i] = string.Concat(
                result.MonsterId,
                " killed=",
                result.Killed,
                " monster=",
                result.WasMonster,
                " hpBefore=",
                result.HealthBefore);
        }

        return string.Join("; ", parts);
    }
}
