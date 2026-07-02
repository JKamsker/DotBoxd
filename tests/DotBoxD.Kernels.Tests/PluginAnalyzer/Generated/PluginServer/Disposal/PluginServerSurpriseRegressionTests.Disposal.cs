using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerSurpriseRegressionTests
{
    [Theory]
    [InlineData("EnsureAnonymousDirect")]
    [InlineData("EnsureAnonymousThroughInterface")]
    public async Task Generated_plugin_server_anonymous_install_rejects_disposed_before_factory(string methodName)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(DisposedSurfaceSource);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var world = Activator.CreateInstance(assembly.GetType("Sample.RecordingWorld", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, world])!;
        var factoryCalls = 0;
        Func<DotBoxD.Plugins.PluginPackage> factory = () =>
        {
            factoryCalls++;
            return null!;
        };

        await DisposeAsync(server);

        var method = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [server, factory])!;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task Generated_plugin_server_disposal_closes_runtime_surfaces()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(DisposedSurfaceSource);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var world = Activator.CreateInstance(assembly.GetType("Sample.RecordingWorld", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, world])!;

        await DisposeAsync(server);

        AssertObjectDisposed(assembly, "ReadHooks", server);
        AssertObjectDisposed(assembly, "ReadSubscriptions", server);
        AssertObjectDisposed(assembly, "ReadCurrentTick", server);
        AssertObjectDisposed(assembly, "ReadInventory", server);
        AssertObjectDisposed(assembly, "ReadLiveSettings", server);
        AssertObjectDisposed(assembly, "Hold", server);
        Assert.Contains("_localHandlers.Clear();", generated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_plugin_server_cached_extension_registry_observes_disposal()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(DisposedSurfaceSource);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Emit(outputCompilation);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var world = Activator.CreateInstance(assembly.GetType("Sample.RecordingWorld", throwOnError: true)!)!;
        var start = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("StartServerWithRegisteredExtension", BindingFlags.Public | BindingFlags.Static)!;
        var server = await AwaitValueTaskResult<object>(start.Invoke(null, [control, world])!);
        var registry = server.GetType().GetProperty("ServerExtensions", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(server)!;

        await DisposeAsync(server);

        var serviceType = assembly.GetType("Sample.IScoreService", throwOnError: true)!;
        var pluginId = registry.GetType().GetMethod("PluginId", BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(serviceType);
        var exception = Assert.Throws<TargetInvocationException>(() => pluginId.Invoke(registry, null));
        Assert.IsType<ObjectDisposedException>(exception.InnerException);
        Assert.Contains(
            "public string PluginId<TService>() where TService : class\n    {\n        ThrowIfDisposed();\n        return _serverExtensions.TryGetValue",
            NormalizeLineEndings(generated),
            StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static Assembly Emit(Microsoft.CodeAnalysis.Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task DisposeAsync(object server)
    {
        var valueTask = server.GetType().GetMethod("DisposeAsync", Type.EmptyTypes)!.Invoke(server, null)!;
        await AwaitValueTask(valueTask);
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        await ((Task)asTask.Invoke(valueTask, null)!).ConfigureAwait(false);
    }

    private static async Task<T> AwaitValueTaskResult<T>(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        return await ((Task<T>)asTask.Invoke(valueTask, null)!).ConfigureAwait(false);
    }

    private static void AssertObjectDisposed(Assembly assembly, string methodName, object server)
    {
        var method = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [server]));
        Assert.IsType<ObjectDisposedException>(exception.InnerException);
    }

    private const string DisposedSurfaceSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample
        {
            [DotBoxDService]
            public interface IInventoryControl
            {
                ValueTask<int> CountAsync(CancellationToken ct = default);
            }

            [DotBoxDService]
            public interface IGameWorldAccess
            {
                int CurrentTick { get; }
                IInventoryControl Inventory { get; }
            }

            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }

            public sealed record DamageEvent(string TargetId, int Amount);

            public interface IScoreService
            {
                ValueTask<int> ReadAsync(CancellationToken ct = default);
            }

            [Plugin("sample-live")]
            public sealed partial class LiveKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 1;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => e.Amount >= MinDamage;
                public void Handle(DamageEvent e, HookContext ctx) => ctx.Messages.Send(e.TargetId, "hit");
            }

            [ServerExtension("score", typeof(IScoreService))]
            public sealed partial class ScoreKernel
            {
                public int Read(HookContext ctx) => 1;
            }

            [GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(IGamePluginControlService))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("score");

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }

            public sealed class RecordingWorld : IGameWorldAccess
            {
                public int CurrentTick => 42;
                public IInventoryControl Inventory { get; } = new InventoryControl();
            }

            public sealed class InventoryControl : IInventoryControl
            {
                public ValueTask<int> CountAsync(CancellationToken ct = default)
                    => ValueTask.FromResult(7);
            }

            public static class Usage
            {
                public static async ValueTask<object> StartServerWithRegisteredExtension(
                    IGamePluginControlService control,
                    IGameWorldAccess world)
                {
                    var server = RemotePluginServerBuilder
                        .FromConnection(control, world)
                        .Setup(setup => setup.Inventory.Extend<IScoreService, ScoreKernel>())
                        .Build();
                    await server.StartAsync();
                    return server;
                }

                public static object ReadHooks(RemotePluginServer server) => server.Hooks;
                public static object ReadSubscriptions(RemotePluginServer server) => server.Subscriptions;
                public static int ReadCurrentTick(RemotePluginServer server) => server.CurrentTick;
                public static object ReadInventory(RemotePluginServer server) => server.Inventory;
                public static object ReadLiveSettings(RemotePluginServer server) => server.Get<LiveKernel>();
                public static ValueTask Hold(RemotePluginServer server) => server.HoldUntilShutdownAsync();
                public static Task<string> EnsureAnonymousDirect(
                    RemotePluginServer server,
                    Func<PluginPackage> factory)
                    => server.EnsureAnonymousKernelAsync("probe", factory);
                public static Task<string> EnsureAnonymousThroughInterface(
                    RemotePluginServer server,
                    Func<PluginPackage> factory)
                    => ((IGameWorldServer)server).EnsureAnonymousKernelAsync("probe", factory);
            }
        }

        namespace Sample.Ipc
        {
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            [DotBoxDService]
            public interface IPluginEventCallback
            {
                ValueTask OnEventAsync(string subscriptionId, ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);
                ValueTask<byte[]> OnResultAsync(string subscriptionId, ReadOnlyMemory<byte> contextValue, CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");

                public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                    DotBoxD.Services.Peer.RpcPeer peer,
                    Sample.Ipc.IPluginEventCallback implementation)
                    => peer;
            }
        }
        """;
}
