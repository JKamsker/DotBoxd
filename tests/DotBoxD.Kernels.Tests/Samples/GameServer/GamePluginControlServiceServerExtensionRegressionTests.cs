using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginControlServiceServerExtensionRegressionTests
{
    [Fact]
    public async Task Generated_plugin_facade_completes_demo_setup_over_real_ipc()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var abstractions = Assembly.LoadFrom(GameServerAbstractionsAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        var worldHost = Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameWorldHost");
        var addBindings = worldHost.GetType().GetMethod(
            "AddBindings",
            BindingFlags.Public | BindingFlags.Instance)!;
        var defaultPolicy = (SandboxPolicy)gameServer
            .GetType("DotBoxD.Kernels.Game.Server.ServerPolicy", throwOnError: true)!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [])!;

        using var server = PluginServer.Create(
            messages: sink,
            configureHost: builder => addBindings.Invoke(worldHost, [builder]),
            defaultPolicy: defaultPolicy,
            executionMode: DotBoxD.Kernels.ExecutionMode.Compiled);
        ResolveEvent(server, abstractions, "DotBoxD.Kernels.Game.Server.Abstractions.Events.MonsterAggroEvent");
        ResolveEvent(server, abstractions, "DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent");

        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
        sink.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)!.Invoke(sink, [world]);
        worldHost.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)!.Invoke(worldHost, [world]);

        var pipeName = "dotboxd-game-" + Guid.NewGuid().ToString("N");
        var serviceCreated = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new RpcPeerOptions { ExceptionTransformer = ex => RpcErrorInfo.FromException(ex) };
        await using var host = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
        {
            PluginSession? session = null;
            try
            {
                session = server.CreateSession();
                peer.Disconnected += (_, _) => session.Dispose();
                var service = Create(
                    gameServer,
                    "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
                    server,
                    session,
                    sink,
                    world);
                InvokeGeneratedProvide(abstractions, "ProvideGamePluginControlService", peer, service);
                InvokeGeneratedProvide(
                    abstractions,
                    "ProvideGameWorldAccess",
                    peer,
                    Create(gameServer, "DotBoxD.Kernels.Game.Server.Ipc.GameWorldAccess", world));
                serviceCreated.TrySetResult(service);
            }
            catch (Exception ex)
            {
                session?.Dispose();
                serviceCreated.TrySetException(ex);
                throw;
            }
        }, options);
        await host.StartAsync();

        var pluginTask = RunPluginMainAsync(pipeName);
        var service = await serviceCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ready = ReadyTask(service);
        var completed = await Task.WhenAny(ready, pluginTask).WaitAsync(TimeSpan.FromSeconds(20));
        if (completed == pluginTask)
        {
            Assert.Fail($"Plugin exited before HoldUntilShutdownAsync; exit code {await pluginTask}.");
        }

        SignalShutdown(service);
        Assert.Equal(0, await pluginTask.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task InvokeServerExtensionAsync_executes_monster_killer_rpc_against_real_world()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        var worldHost = Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameWorldHost");
        var addBindings = worldHost.GetType().GetMethod(
            "AddBindings",
            BindingFlags.Public | BindingFlags.Instance)!;
        var defaultPolicy = (SandboxPolicy)gameServer
            .GetType("DotBoxD.Kernels.Game.Server.ServerPolicy", throwOnError: true)!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [])!;

        using var server = PluginServer.Create(
            messages: sink,
            configureHost: builder => addBindings.Invoke(worldHost, [builder]),
            defaultPolicy: defaultPolicy,
            executionMode: DotBoxD.Kernels.ExecutionMode.Compiled);
        var world = gameServer
            .GetType("DotBoxD.Kernels.Game.Server.Simulation.GameWorld", throwOnError: true)!
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [server.Hooks, server.Subscriptions])!;
        sink.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)!.Invoke(sink, [world]);
        worldHost.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)!.Invoke(worldHost, [world]);
        var service = Create(
            gameServer,
            "DotBoxD.Kernels.Game.Server.Ipc.GamePluginControlService",
            server,
            server.CreateSession(),
            sink,
            world);
        var package = ResolveGamePluginPackage("DotBoxD.Kernels.Game.Plugin.Kernels.MonsterKillerKernel");
        var pluginId = await InstallServerExtensionAsync(service, PluginPackageJsonSerializer.Export(package));
        var killed = world.GetType()
            .GetMethod("KillMonster", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(world, ["monster-4"]);
        Assert.True((bool)killed!);
        var request = KernelRpcBinaryCodec.EncodeArguments(
        [
            KernelRpcValue.List(
            [
                KernelRpcValue.String("monster-3"),
                KernelRpcValue.String("monster-4"),
                KernelRpcValue.String("player-1")
            ])
        ]);

        var response = await InvokeServerExtensionAsync(service, pluginId, request);

        var results = KernelRpcBinaryCodec.DecodeValue(response);
        results.RequireKind(KernelRpcValueKind.List);
        Assert.Equal(3, results.ItemCount);
        AssertKillResult(results.GetItem(0), "monster-3", wasMonster: true, killed: true);
        AssertKillResult(results.GetItem(1), "monster-4", wasMonster: true, killed: false);
        AssertKillResult(results.GetItem(2), "player-1", wasMonster: false, killed: false);
    }

    private static void AssertKillResult(
        KernelRpcValue result,
        string monsterId,
        bool wasMonster,
        bool killed)
    {
        result.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(monsterId, result.GetItem(0).TextValue);
        Assert.Equal(wasMonster, result.GetItem(1).BoolValue);
        Assert.Equal(killed, result.GetItem(5).BoolValue);
    }

    private static object Create(Assembly assembly, string typeName, params object[] args)
        => Activator.CreateInstance(
            assembly.GetType(typeName, throwOnError: true)!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args,
            culture: null)!;

    private static async Task<string> InstallServerExtensionAsync(object service, string json)
    {
        var result = service.GetType()
            .GetMethod("InstallServerExtensionAsync", BindingFlags.Public | BindingFlags.Instance)!
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

    private static PluginPackage ResolveGamePluginPackage(string typeName)
    {
        var kernelType = Assembly.LoadFrom(GamePluginAssemblyPath()).GetType(typeName, throwOnError: true)!;
        return KernelPackageRegistry.Resolve(kernelType);
    }

    private static Task<int> RunPluginMainAsync(string pipeName)
    {
        var program = Assembly.LoadFrom(GamePluginAssemblyPath())
            .GetType("DotBoxD.Kernels.Game.Plugin.Program", throwOnError: true)!;
        var result = program
            .GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new[] { pipeName }]);
        return (Task<int>)result!;
    }

    private static void InvokeGeneratedProvide(
        Assembly generatedAssembly,
        string methodName,
        RpcPeer peer,
        object implementation)
    {
        var extensions = generatedAssembly.GetType("DotBoxD.Services.Generated.DotBoxDGeneratedExtensions", throwOnError: true)!;
        var method = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = candidate.GetParameters();
                return parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(RpcPeer) &&
                    parameters[1].ParameterType.IsInstanceOfType(implementation);
            });
        method.Invoke(null, [peer, implementation]);
    }

    private static void ResolveEvent(PluginServer server, Assembly abstractions, string eventTypeName)
    {
        var eventType = abstractions.GetType(eventTypeName, throwOnError: true)!;
        server.Events.GetType()
            .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(eventType)
            .Invoke(server.Events, []);
    }

    private static Task ReadyTask(object service)
        => (Task)service.GetType()
            .GetProperty("Ready", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(service)!;

    private static void SignalShutdown(object service)
        => service.GetType()
            .GetMethod("SignalShutdown", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(service, []);

    private static string GameServerAssemblyPath()
        => SampleAssemblyPath("Examples.GameServer.Server", "Examples.GameServer.Server.dll");

    private static string GamePluginAssemblyPath()
        => SampleAssemblyPath("Examples.GameServer.Plugin", "Examples.GameServer.Plugin.dll");

    private static string GameServerAbstractionsAssemblyPath()
        => SampleAssemblyPath(
            "Examples.GameServer.Server.Abstractions",
            "Examples.GameServer.Server.Abstractions.dll");

    private static string SampleAssemblyPath(string project, string fileName)
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
            project,
            "bin",
            configuration,
            "net10.0",
            fileName));
    }
}
