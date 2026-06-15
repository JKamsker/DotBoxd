using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Ipc;
using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Server;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server;

using System.Globalization;
using DotBoxD.Services;

/// <summary>
/// The game server (parent process): a deterministic 1D simulation that runs a baseline phase with no
/// plugins, launches the plugin child process to ship verified kernels over IPC, then runs a
/// with-plugin phase showing the sandboxed kernels change behavior.
/// </summary>
internal static class Program
{
    private const int BaselineTicks = 3;
    private const int PluginTicks = 4;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--use-builder"))
        {
            await Console.Error
                .WriteLineAsync("Usage: Examples.GameServer.Server [--use-builder]")
                .ConfigureAwait(false);
            return 1;
        }

        var useBuilder = args.Length == 1;

        // (a) Build the world and plugin server. The command sink is the example-defined capability
        // that turns plugin messages into game-state changes; the world host backs the gated
        // ctx.Host<IGameWorldAccess>() read bindings. Both are bound to the world once it exists.
        var sink = new GameCommandSink();
        var worldHost = new GameWorldHost();
        // Compiled mode: the plugin's verified IR is JIT-compiled to fast, verifier-checked IL (rather
        // than interpreted) — proving the IR library compiles valid IL from the kernels this plugin ships.
        using var server = PluginServer.Create(
            sink,
            configureHost: worldHost.AddBindings,
            defaultPolicy: ServerPolicy.Create(),
            executionMode: ExecutionMode.Compiled);

        // Register convention adapters for the events plugins may subscribe to. No hand-written
        // adapter is needed — the sandbox shape is inferred from each record's properties. Resolving
        // them up front lets prepared-package validation check kernel parameter shapes at install.
        _ = server.Events.Resolve<MonsterAggroEvent>();
        _ = server.Events.Resolve<AttackEvent>();

        var world = GameWorld.CreateDefault(server.Hooks);
        sink.Bind(world);
        worldHost.Bind(world);

        var playerHpBaseline = PlayerHpById(world);

        Console.WriteLine("=== DotBoxD.Kernels Game Server (golden example) ===");
        Console.WriteLine("Low-level players face nearby lvl8 monsters; two remote monsters are used by the RPC demo.");
        Console.WriteLine();

        // (b) Baseline phase: no plugins. Monsters bully the low-level players.
        Console.WriteLine("--- BASELINE (no plugins) ---");
        PrintWorld(world);
        for (var i = 0; i < BaselineTicks; i++)
        {
            await world.TickAsync().ConfigureAwait(false);
            Console.WriteLine($"[tick {world.Tick}]");
            PrintWorld(world);
        }

        var baselineDamage = TotalDamageTaken(playerHpBaseline, world);
        Console.WriteLine($"Baseline: low-level players took {baselineDamage} total damage in {BaselineTicks} ticks.");
        Console.WriteLine();

        // (c) Start the IPC control plane on a high-entropy pipe name. Each peer gets its own ownership
        // session; when the connection drops, the session is disposed and the kernels it owned unload.
        var pipeName = "dotboxd-game-" + Guid.NewGuid().ToString("N");
        var connected = new TaskCompletionSource<GamePluginControlService>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = RpcMessagePackIpc.ListenNamedPipe(
            pipeName,
            peer =>
            {
                var session = server.CreateSession();
                var service = new GamePluginControlService(server, session, sink, world);
                peer.Disconnected += (_, _) =>
                {
                    session.Dispose();           // revoke + unregister the kernels this connection owned
                    disconnected.TrySetResult();
                };
                peer.ProvideGamePluginControlService(service);
                connected.TrySetResult(service);
            });
        await host.StartAsync().ConfigureAwait(false);
        Console.WriteLine($"[server] listening for plugin on pipe '{pipeName}'.");

        // (d) Launch the plugin child process.
        Console.WriteLine("[server] launching plugin child process...");
        var pluginProcess = PluginLauncher.Launch(pipeName, useBuilder);
        var pluginExit = pluginProcess.WaitForExitAsync();

        // (e) Wait until the plugin has connected and installed its kernels (it then holds the
        // connection). Fail fast if it exits early.
        if (await Task.WhenAny(connected.Task, pluginExit).ConfigureAwait(false) == pluginExit && !connected.Task.IsCompleted)
        {
            return await FailAsync(host, $"plugin exited before connecting (code {pluginProcess.ExitCode}).").ConfigureAwait(false);
        }

        var control = await connected.Task.ConfigureAwait(false);
        if (await Task.WhenAny(control.Ready, pluginExit).ConfigureAwait(false) == pluginExit && !control.Ready.IsCompleted)
        {
            return await FailAsync(host, $"plugin exited before installing kernels (code {pluginProcess.ExitCode}).").ConfigureAwait(false);
        }

        Console.WriteLine("[server] plugin connected; event kernels and server extension are installed and live.");
        Console.WriteLine("[server] Running with-plugin phase after the plugin's direct IPC/RPC setup calls.");
        Console.WriteLine();

        // (f) With-plugin phase: the untrusted kernels run sandboxed WHILE the plugin is connected.
        Console.WriteLine("--- WITH PLUGINS (guardian calms, retaliation taunts) ---");
        var pluginPhaseStart = PlayerHpById(world);
        for (var i = 0; i < PluginTicks; i++)
        {
            await world.TickAsync().ConfigureAwait(false);
            Console.WriteLine($"[tick {world.Tick}]");
            PrintEffects(sink.DrainEffects());
            PrintWorld(world);
        }

        var pluginDamage = TotalDamageTaken(pluginPhaseStart, world);
        var perTickBaseline = (double)baselineDamage / BaselineTicks;
        var perTickPlugin = (double)pluginDamage / PluginTicks;

        // (g) Release the plugin; it disconnects, and ownership unloads its kernels.
        control.SignalShutdown();
        await pluginExit.ConfigureAwait(false);
        await disconnected.Task.ConfigureAwait(false);
        if (pluginProcess.ExitCode != 0)
        {
            return await FailAsync(host, $"plugin exited with code {pluginProcess.ExitCode}.").ConfigureAwait(false);
        }

        // (h) Summary, plus proof that disconnect unloaded the plugin's kernels.
        Console.WriteLine();
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine(Format("Baseline damage/tick (no plugin)", perTickBaseline));
        Console.WriteLine(Format("With-plugin damage/tick", perTickPlugin));
        Console.WriteLine(perTickPlugin < perTickBaseline
            ? "Plugins reduced bullying: low-level players survive longer than baseline."
            : "Plugins applied (see per-tick effects above).");
        Console.WriteLine($"On disconnect the plugin's kernels were unloaded (installed kernels now: {server.Kernels.Snapshot().Count}).");
        PrintSurvivors(world);

        await host.StopAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> FailAsync(RpcHost host, string message)
    {
        await Console.Error.WriteLineAsync($"[server] {message}").ConfigureAwait(false);
        await host.StopAsync().ConfigureAwait(false);
        return 1;
    }

    private static Dictionary<string, int> PlayerHpById(GameWorld world)
        => world.Players().ToDictionary(p => p.Id, p => p.Hp, StringComparer.Ordinal);

    private static int TotalDamageTaken(IReadOnlyDictionary<string, int> before, GameWorld world)
    {
        var total = 0;
        foreach (var player in world.Players())
        {
            if (before.TryGetValue(player.Id, out var previous))
            {
                total += Math.Max(0, previous - player.Hp);
            }
        }

        return total;
    }

    private static void PrintWorld(GameWorld world)
        => Console.WriteLine(world.Render());

    private static void PrintEffects(IReadOnlyList<string> effects)
    {
        if (effects.Count == 0)
        {
            Console.WriteLine("    (no plugin effects applied this tick)");
            return;
        }

        foreach (var effect in effects)
        {
            Console.WriteLine($"    effect: {effect}");
        }
    }

    private static void PrintSurvivors(GameWorld world)
    {
        foreach (var player in world.Players())
        {
            var state = player.IsAlive ? $"alive (hp {player.Hp})" : "defeated";
            Console.WriteLine($"    {player.Id}: {state}");
        }
    }

    private static string Format(string label, double value)
        => $"{label}: {value.ToString("0.0", CultureInfo.InvariantCulture)}";
}
