using System.Reflection;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginControlServiceTests
{
    [Fact]
    public async Task InstallPluginAsync_rolls_back_installed_kernel_when_hook_wiring_fails()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        using var server = PluginServer.Create(sink);
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks])!;
        sink.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)!.Invoke(sink, [world]);
        var session = server.CreateSession();
        var service = Create(
            gameServer,
            "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
            server,
            session,
            sink,
            world);
        var json = PluginPackageJsonSerializer.Export(FireDamagePluginPackage.Create());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await InstallPluginAsync(service, json));

        Assert.False(session.Owns("fire-damage"));
        Assert.False(server.Kernels.TryGet("fire-damage", out _));
    }

    [Fact]
    public async Task HoldUntilShutdownAsync_observes_cancellation()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        using var server = PluginServer.Create(sink);
        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks])!;
        var service = Create(
            gameServer,
            "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
            server,
            server.CreateSession(),
            sink,
            world);
        using var cts = new CancellationTokenSource();
        var hold = HoldUntilShutdownAsync(service, cts.Token).AsTask();

        try
        {
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await hold.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            service.GetType().GetMethod("SignalShutdown", BindingFlags.Public | BindingFlags.Instance)!.Invoke(service, []);
            await Task.WhenAny(hold, Task.Delay(TimeSpan.FromSeconds(1)));
        }
    }

    private static object Create(Assembly assembly, string typeName, params object[] args)
        => Activator.CreateInstance(
            assembly.GetType(typeName, throwOnError: true)!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args,
            culture: null)!;

    private static async Task<string> InstallPluginAsync(object service, string json)
    {
        var result = service.GetType()
            .GetMethod("InstallPluginAsync", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(service, [json, CancellationToken.None]);
        return await ((ValueTask<string>)result!).ConfigureAwait(false);
    }

    private static ValueTask HoldUntilShutdownAsync(object service, CancellationToken cancellationToken)
    {
        var result = service.GetType()
            .GetMethod("HoldUntilShutdownAsync", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(service, [cancellationToken]);
        return (ValueTask)result!;
    }

    private static string GameServerAssemblyPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "Kernels",
            "GameServer",
            "DotBoxD.Kernels.Game.Server",
            "bin",
            configuration,
            "net10.0",
            "DotBoxD.Kernels.Game.Server.dll"));
    }
}
