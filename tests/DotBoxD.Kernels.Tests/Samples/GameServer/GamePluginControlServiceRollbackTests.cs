using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginControlServiceRollbackTests
{
    [Fact]
    public async Task InstallPluginAsync_rejects_local_terminal_without_callback_before_install()
    {
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var package = LocalTerminalPackage("local-calm");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(package)));

            Assert.False(session.Owns("local-calm"));
            Assert.DoesNotContain(server.Kernels.Snapshot(), kernel => kernel.Manifest.PluginId == "local-calm");
        }
    }

    [Fact]
    public async Task Failed_local_terminal_hot_replace_keeps_existing_kernel()
    {
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var incumbentPackage = ResolveGamePluginPackage("DotBoxD.Kernels.Game.Plugin.Kernels.RetaliationKernel");
            await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(incumbentPackage));
            var incumbent = server.Kernels.Get("retaliation");
            var replacement = LocalTerminalPackage("retaliation");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(replacement)));

            Assert.True(session.Owns("retaliation"));
            Assert.True(server.Kernels.TryGet("retaliation", out var installed));
            Assert.Same(incumbent, installed);
            Assert.False(incumbent.IsRevoked);
        }
    }

    [Fact]
    public async Task InstallPluginAsync_rolls_back_kernel_when_hook_wiring_fails_after_install()
    {
        var (server, session, service) = CreateControlService();
        using (server)
        {
            var package = ResultHookOnNonResultEventPackage("post-wire-fail");

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await InstallPluginAsync(service, PluginPackageJsonSerializer.Export(package)));

            Assert.False(session.Owns("post-wire-fail"));
            Assert.DoesNotContain(server.Kernels.Snapshot(), kernel => kernel.Manifest.PluginId == "post-wire-fail");
        }
    }

    private static (PluginServer Server, PluginSession Session, object Service) CreateControlService()
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var sink = (IPluginMessageSink)Create(gameServer, "DotBoxD.Kernels.Game.Server.Simulation.GameCommandSink");
        var server = PluginServer.Create(sink);
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
        return (server, session, service);
    }

    private static PluginPackage LocalTerminalPackage(string pluginId)
    {
        var span = new SourceSpan(1, 1);
        var manifest = new PluginManifest(
            pluginId,
            "IEventKernel<DotBoxD.Kernels.Game.Server.Abstractions.Events.MonsterAggroEvent>",
            ExecutionMode.Auto,
            ["Alloc", "Cpu"],
            [],
            [
                new HookSubscriptionManifest(
                    "DotBoxD.Kernels.Game.Server.Abstractions.Events.MonsterAggroEvent",
                    pluginId)
                {
                    LocalTerminal = true,
                    ProjectedType = "string"
                }
            ]);
        var module = new SandboxModule(
            pluginId,
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [ShouldHandle(span), Handle(span)],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["callbackSubscriptionId"] = pluginId + "-callback",
                ["kernel"] = pluginId,
                ["pluginId"] = pluginId
            });
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static PluginPackage ResultHookOnNonResultEventPackage(string pluginId)
    {
        var manifest = new PluginManifest(
            pluginId,
            "IEventKernel<DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent>",
            ExecutionMode.Auto,
            ["Cpu"],
            [],
            [
                new HookSubscriptionManifest(
                    "DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent",
                    pluginId)
                {
                    ResultType = "DotBoxD.Kernels.Game.Server.Abstractions.Events.RemoteDamageDecisionResult"
                }
            ]);
        var module = new SandboxModule(
            pluginId,
            SemVersion.One,
            SemVersion.One,
            [],
            [AttackShouldHandle(), AttackResultHandle()],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kernel"] = pluginId,
                ["pluginId"] = pluginId
            });
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static SandboxFunction ShouldHandle(SourceSpan span)
        => new(
            "ShouldHandle",
            IsEntrypoint: true,
            EventParameters(),
            SandboxType.Bool,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), span), span)]);

    private static SandboxFunction Handle(SourceSpan span)
        => new(
            "Handle",
            IsEntrypoint: true,
            EventParameters(),
            SandboxType.String,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromString("monster-1"), span), span)]);

    private static SandboxFunction AttackShouldHandle()
        => new(
            "ShouldHandle",
            IsEntrypoint: true,
            AttackEventParameters(),
            SandboxType.Bool,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), new SourceSpan(1, 1)), new SourceSpan(1, 1))]);

    private static SandboxFunction AttackResultHandle()
        => new(
            "Handle",
            IsEntrypoint: true,
            AttackEventParameters(),
            SandboxType.Bool,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), new SourceSpan(1, 1)), new SourceSpan(1, 1))]);

    private static Parameter[] EventParameters()
        =>
        [
            new("e_MonsterId", SandboxType.String),
            new("e_PlayerId", SandboxType.String),
            new("e_Distance", SandboxType.I32),
            new("e_MonsterLevel", SandboxType.I32),
            new("e_PlayerLevel", SandboxType.I32)
        ];

    private static Parameter[] AttackEventParameters()
        =>
        [
            new("e_AttackerId", SandboxType.String),
            new("e_TargetId", SandboxType.String),
            new("e_Damage", SandboxType.I32),
            new("e_AttackerLevel", SandboxType.I32)
        ];

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

    private static PluginPackage ResolveGamePluginPackage(string typeName)
    {
        var kernelType = Assembly.LoadFrom(GamePluginAssemblyPath()).GetType(typeName, throwOnError: true)!;
        return KernelPackageRegistry.Resolve(kernelType);
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
}
