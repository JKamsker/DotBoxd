using System.Reflection;
using System.Xml.Linq;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

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
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
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
    public async Task InstallPluginAsync_failed_hot_replace_keeps_existing_kernel()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        using var server = PluginServer.Create(sink);
        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
        sink.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)!.Invoke(sink, [world]);
        var session = server.CreateSession();
        var service = Create(
            gameServer,
            "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
            server,
            session,
            sink,
            world);
        var retaliation = ResolveGamePluginPackage("DotBoxD.Kernels.Game.Plugin.Kernels.RetaliationKernel");
        var subscription = Assert.Single(retaliation.Manifest.Subscriptions);
        var unsupportedReplacement = retaliation with
        {
            Manifest = retaliation.Manifest with
            {
                Contract = "IEventKernel<DamageEvent>",
                Subscriptions = [new HookSubscriptionManifest("DamageEvent", subscription.Kernel)]
            }
        };

        await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(retaliation));
        var incumbent = server.Kernels.Get("retaliation");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(unsupportedReplacement)));

        Assert.True(session.Owns("retaliation"));
        Assert.True(server.Kernels.TryGet("retaliation", out var installed));
        Assert.Same(incumbent, installed);
        Assert.False(incumbent.IsRevoked);
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
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
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

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await hold.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            service.GetType().GetMethod("SignalShutdown", BindingFlags.Public | BindingFlags.Instance)!.Invoke(service, []);
            await Task.WhenAny(hold, Task.Delay(TimeSpan.FromSeconds(1)));
        }
    }

    [Fact]
    public async Task HoldUntilShutdownAsync_does_not_mark_ready_when_precancelled()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        using var server = PluginServer.Create(sink);
        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
        var service = Create(
            gameServer,
            "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
            server,
            server.CreateSession(),
            sink,
            world);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await HoldUntilShutdownAsync(service, cts.Token));

        Assert.False(ReadyTask(service).IsCompleted);
    }

    [Fact]
    public async Task InvokeServerExtensionAsync_requires_session_owned_server_extension()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        using var server = PluginServer.Create(sink);
        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
        var service = Create(
            gameServer,
            "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
            server,
            server.CreateSession(),
            sink,
            world);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await InvokeServerExtensionAsync(service, "monster-killer", []));

        Assert.Contains("not owned by this plugin session", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GameServer_project_builds_child_plugin_project()
    {
        var project = XDocument.Load(GameServerProjectPath());
        Assert.DoesNotContain(project.Descendants("ProjectReference"), IsGamePluginReference);
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

    private static async Task<byte[]> InvokeServerExtensionAsync(object service, string pluginId, byte[] arguments)
    {
        var result = service.GetType()
            .GetMethod("InvokeServerExtensionAsync", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(service, [pluginId, arguments, CancellationToken.None]);
        return await ((ValueTask<byte[]>)result!).ConfigureAwait(false);
    }

    private static ValueTask HoldUntilShutdownAsync(object service, CancellationToken cancellationToken)
    {
        var result = service.GetType()
            .GetMethod("HoldUntilShutdownAsync", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(service, [cancellationToken]);
        return (ValueTask)result!;
    }

    private static Task ReadyTask(object service)
        => (Task)service.GetType()
            .GetProperty("Ready", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(service)!;

    private static PluginPackage ResolveGamePluginPackage(string typeName)
    {
        var kernelType = Assembly.LoadFrom(GamePluginAssemblyPath()).GetType(typeName, throwOnError: true)!;
        return KernelPackageRegistry.Resolve(kernelType);
    }

    private static bool IsGamePluginReference(XElement reference)
    {
        var include = ((string?)reference.Attribute("Include"))?.Replace('\\', '/');
        return include?.EndsWith(
            "/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj",
            StringComparison.Ordinal) is true;
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
            "GameServer",
            "Examples.GameServer.Server",
            "bin",
            configuration,
            "net10.0",
            "Examples.GameServer.Server.dll"));
    }

    private static string GamePluginAssemblyPath()
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
            "GameServer",
            "Examples.GameServer.Plugin",
            "bin",
            configuration,
            "net10.0",
            "Examples.GameServer.Plugin.dll"));
    }

    private static string GameServerProjectPath()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            "Examples.GameServer.Server",
            "Examples.GameServer.Server.csproj"));
}
