namespace DotBoxD.Kernels.Game.Plugin;

using DotBoxD.Kernels.Game.Plugin.Client;
using DotBoxD.Kernels.Transport.Ipc;

/// <summary>
/// The plugin process. Connects to the game server's control plane, ships each kernel as verified IR
/// (the server never sees kernel source), tunes live settings over IPC, then exits so the server
/// proceeds to its with-plugin phase.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: DotBoxD.Kernels.Game.Plugin <named-pipe-name>").ConfigureAwait(false);
            return 1;
        }

        var pipeName = args[0];

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

        // Tune live settings — strongly typed, one atomic IPC batch under the hood.
        await server.Kernels.Get<GuardianKernel>()
            .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true)
            .ConfigureAwait(false);
        Console.WriteLine("[plugin] tuned guardian live settings (CalmStrength=35, AggroRange=6).");

        // Ordinary IPC can expose direct server APIs too. This call goes straight to the server's
        // world service and kills one monster in one IPC call.
        var directKilled = await server.World.KillMonsterAsync("monster-4").ConfigureAwait(false);
        Console.WriteLine($"[plugin] ordinary IPC KillMonster(monster-4) => {directKilled}.");

        // Kernel RPC keeps the plugin-owned loop on the server. The plugin sends one request, the
        // server executes the generated verified IR, and the result list comes back as plugin DTOs.
        var monsterKiller = MonsterKillerKernelRpcClient.Create(server.KernelRpc, killerId);
        var killResults = await monsterKiller
            .KillMonstersAsync(["monster-3", "monster-4", "player-1", "monster-missing"])
            .ConfigureAwait(false);
        Console.WriteLine("[plugin] kernel RPC KillMonsters(...) => " + FormatKillResults(killResults));

        // Hold the connection open so the kernels stay owned and live while the server runs its
        // with-plugin phase. When the server signals shutdown this returns and we disconnect, at which
        // point the server unloads our kernels (ownership = connection lifetime).
        Console.WriteLine("[plugin] kernels live; holding connection until server completes...");
        await server.HoldUntilShutdownAsync().ConfigureAwait(false);

        Console.WriteLine("[plugin] released by server. Disconnecting (kernels will be unloaded). Exiting.");
        return 0;
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
