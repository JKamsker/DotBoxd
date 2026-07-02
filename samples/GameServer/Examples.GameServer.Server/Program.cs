using System.Diagnostics;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Game.Server.Ipc;
using DotBoxD.Kernels.Game.Server.PluginCatalog;
using DotBoxD.Kernels.Game.Server.Simulation;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server;

internal static class Program
{
    private const int BaselineTicks = 3;

    public static async Task<int> Main(string[] args)
    {
        if (!TryParse(args, out var listenPort, out var launchClient))
        {
            await Console.Error.WriteLineAsync(
                "Usage: Examples.GameServer.Server [--listen <port>] [--no-launch]");
            return 1;
        }

        var pluginsRoot = PluginsRoot();
        if (!Directory.Exists(pluginsRoot))
        {
            await Console.Error.WriteLineAsync($"Plugin bundles missing: {pluginsRoot}");
            return 1;
        }

        var sink = new GameCommandSink();
        var worldHost = new GameWorldHost();
        using var server = PluginServer.Create(
            sink,
            configureHost: worldHost.AddBindings,
            defaultPolicy: ServerPolicy.Create(),
            executionMode: ExecutionMode.Compiled);

        RegisterEvents(server);
        var world = GameWorld.CreateDefault(server.Hooks, server.Subscriptions);
        sink.Bind(world);
        worldHost.Bind(world);

        Console.WriteLine("=== DotBoxD.Kernels Game Server (vendor server + vendor client) ===");
        Console.WriteLine("[server] real vendors put authenticated session identity + TLS on this TCP link.");
        Console.WriteLine();

        var operations = new ClientOperationRegistry();
        Console.WriteLine("--- SERVER INSTALL ---");
        await new PluginCatalogInstaller(server, world, operations)
            .InstallServerPartsAsync(pluginsRoot)
            .ConfigureAwait(false);
        Console.WriteLine();

        Console.WriteLine("--- BASELINE ---");
        await RunTicksAsync(world, BaselineTicks).ConfigureAwait(false);
        Console.WriteLine("[server] baseline complete; monster-1 is dead and its bounty is unclaimed.");
        Console.WriteLine();

        await using var host = await GameClientConnectionHost
            .StartAsync(server, sink, world, operations, listenPort)
            .ConfigureAwait(false);
        Console.WriteLine($"[server] listening on 127.0.0.1:{host.Port}");
        Process? client = launchClient ? ClientLauncher.Launch(host.Port, pluginsRoot) : null;

        GameClientControlService control;
        try
        {
            control = await host.Connected.WaitAsync(ClientConnectTimeout()).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            client?.Kill(entireProcessTree: true);
            await Console.Error.WriteLineAsync("client did not connect before timeout.").ConfigureAwait(false);
            return 1;
        }

        Console.WriteLine("[server] client connected.");
        var refusal = await control.InstallServerExtensionAsync("{}", CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"[server] scripted extension install attempt => {refusal}");

        await Task.Delay(1_200).ConfigureAwait(false);
        Console.WriteLine("[server] OPERATOR RETUNE MaxBountyPerKill=30");
        if (operations.TryResolve("bounty.claim", out var bounty))
        {
            await bounty.ModifySettingsAsync(
                new Dictionary<string, object?> { ["MaxBountyPerKill"] = 30 },
                atomic: true).ConfigureAwait(false);
        }

        Console.WriteLine("[server] triggering plugin-visible kills for monster-2/3/4.");
        world.KillMonster("monster-2");
        world.KillMonster("monster-3");
        world.KillMonster("monster-4");

        await Task.Delay(3_000).ConfigureAwait(false);
        control.SignalShutdown();
        if (client is not null)
        {
            try
            {
                await WaitForClientAsync(client, ClientShutdownTimeout()).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await Console.Error.WriteLineAsync("client did not shut down before timeout.").ConfigureAwait(false);
                return 1;
            }
        }

        await host.Disconnected.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine($"Balances: {string.Join(", ", world.Economy.Balances().Select(p => p.Key + '=' + p.Value))}");
        Console.WriteLine($"Claims: {string.Join(", ", world.Economy.Claims())}");
        Console.WriteLine($"Client disconnected; installed server kernels now: {server.Kernels.Snapshot().Count}.");
        await host.StopAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task RunTicksAsync(GameWorld world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            await world.TickAsync().ConfigureAwait(false);
            Console.WriteLine($"[tick {world.Tick}]");
            Console.WriteLine(world.Render());
        }
    }

    private static void RegisterEvents(PluginServer server)
    {
        _ = server.Events.Resolve<MonsterAggroEvent>();
        _ = server.Events.Resolve<AttackEvent>();
        _ = server.Events.Resolve<RemoteDamageDecisionEvent>();
        _ = server.Events.Resolve<MonsterKilledEvent>();
        _ = server.Events.Resolve<GoldChangedEvent>();
    }

    private static async Task WaitForClientAsync(Process client, TimeSpan timeout)
    {
        var exit = client.WaitForExitAsync();
        if (await Task.WhenAny(exit, Task.Delay(timeout)).ConfigureAwait(false) != exit)
        {
            client.Kill(entireProcessTree: true);
            throw new TimeoutException();
        }

        await exit.ConfigureAwait(false);
        if (client.ExitCode != 0)
        {
            throw new InvalidOperationException($"client exited with code {client.ExitCode}.");
        }
    }

    private static TimeSpan ClientConnectTimeout()
        => TimeoutFromEnvironment("DOTBOXD_GAME_CLIENT_CONNECT_TIMEOUT_MS", TimeSpan.FromSeconds(15));

    private static TimeSpan ClientShutdownTimeout()
        => TimeoutFromEnvironment("DOTBOXD_GAME_CLIENT_SHUTDOWN_TIMEOUT_MS", TimeSpan.FromSeconds(15));

    private static TimeSpan TimeoutFromEnvironment(string variableName, TimeSpan fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(variableName), out var milliseconds) && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : fallback;

    private static bool TryParse(string[] args, out int listenPort, out bool launchClient)
    {
        listenPort = 0;
        launchClient = true;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--no-launch", StringComparison.Ordinal))
            {
                launchClient = false;
            }
            else if (string.Equals(args[i], "--listen", StringComparison.Ordinal) &&
                     i + 1 < args.Length &&
                     int.TryParse(args[++i], out listenPort))
            {
            }
            else
            {
                return false;
            }
        }

        return listenPort >= 0;
    }

    private static string PluginsRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "plugins"));
}
