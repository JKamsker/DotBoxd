namespace SafeIR.Game.Plugin;

using SafeIR.Game.Plugin.Client;
using SafeIR.Transport.Ipc;

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
            await Console.Error.WriteLineAsync("Usage: SafeIR.Game.Plugin <named-pipe-name>").ConfigureAwait(false);
            return 1;
        }

        var pipeName = args[0];

        // Connect to the game server's control plane and wrap it in a server-shaped shim.
        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");
        await using var connection = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName).ConfigureAwait(false);
        var server = new RemotePluginServer(connection.Get<IGamePluginControlService>());

        // Register each kernel as the implementation of a server service contract. Register resolves
        // the kernel's generated verified IR and ships it — no Export/InstallPluginAsync plumbing.
        var guardianId = await server.Kernels.Register<IMonsterAggroService, GuardianKernel>().ConfigureAwait(false);
        Console.WriteLine($"[plugin] registered kernel '{guardianId}'.");

        var retaliationId = await server.Kernels.Register<IAttackService, RetaliationKernel>().ConfigureAwait(false);
        Console.WriteLine($"[plugin] registered kernel '{retaliationId}'.");

        // Tune live settings — strongly typed, one atomic IPC batch under the hood.
        await server.Kernels.Get<GuardianKernel>()
            .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true)
            .ConfigureAwait(false);
        Console.WriteLine("[plugin] tuned guardian live settings (CalmStrength=35, AggroRange=6).");

        // Hold the connection open so the kernels stay owned and live while the server runs its
        // with-plugin phase. When the server signals shutdown this returns and we disconnect, at which
        // point the server unloads our kernels (ownership = connection lifetime).
        Console.WriteLine("[plugin] kernels live; holding connection until server completes...");
        await server.HoldUntilShutdownAsync().ConfigureAwait(false);

        Console.WriteLine("[plugin] released by server. Disconnecting (kernels will be unloaded). Exiting.");
        return 0;
    }
}
