using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsInstallationSurpriseTests
{
    [Fact]
    public async Task Generated_live_settings_handle_rejects_kernel_type_that_was_never_installed()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Regression.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace Regression.Game.Ipc
            {
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

                public sealed class RecordingControl : IGamePluginControlService
                {
                    public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask UpdateSettingsAsync(
                        string pluginId,
                        LiveSettingUpdate[] updates,
                        bool atomic = false,
                        CancellationToken ct = default)
                        => ValueTask.CompletedTask;

                    public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                        => throw new InvalidOperationException("not used");

                    public ValueTask<byte[]> InvokeServerExtensionAsync(
                        string pluginId,
                        byte[] arguments,
                        CancellationToken cancellationToken = default)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace Regression.Plugin
            {
                using Regression.Game;
                using Regression.Game.Ipc;

                public sealed record DamageEvent(string TargetId);

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;

                [Plugin("guardian")]
                public sealed partial class GuardianKernel : IEventKernel<DamageEvent>
                {
                    [LiveSetting]
                    public int AggroRange { get; set; } = 5;

                    public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                    public void Handle(DamageEvent e, HookContext ctx)
                        => ctx.Messages.Send(e.TargetId, "ok");
                }

                public static class LiveSettingsProbe
                {
                    public static async Task RunAsync()
                    {
                        var server = new RemotePluginServer(new RecordingControl(), world: null);
                        await server.Get<GuardianKernel>().SetValuesAsync(kernel => kernel.AggroRange = 6);
                    }
                }
            }
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Regression.Plugin.LiveSettingsProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(runAsync));
        Assert.Contains("installed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Assembly Load(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task InvokeAsync(MethodInfo method)
    {
        var result = method.Invoke(null, null);
        await Assert.IsAssignableFrom<Task>(result);
    }
}
