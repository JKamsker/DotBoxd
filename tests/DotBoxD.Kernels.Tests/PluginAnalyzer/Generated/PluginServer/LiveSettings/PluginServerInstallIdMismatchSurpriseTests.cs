using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerInstallIdMismatchSurpriseTests
{
    [Fact]
    public async Task Setup_replay_rejects_control_plane_install_id_that_differs_from_manifest_id()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var probe = assembly.GetType("Sample.Plugin.InstallMismatchProbe", throwOnError: true)!;
        var runAsync = probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;

        var result = await Assert.IsAssignableFrom<Task<object>>(runAsync.Invoke(null, null));

        Assert.Equal(typeof(InvalidOperationException).FullName, Get(result, "StartExceptionType"));
        var startMessage = Get(result, "StartExceptionMessage");
        Assert.Contains("guardian", startMessage, StringComparison.Ordinal);
        Assert.Contains("wrong-id", startMessage, StringComparison.Ordinal);

        Assert.Equal(typeof(InvalidOperationException).FullName, Get(result, "UpdateExceptionType"));
        Assert.Contains("installed", Get(result, "UpdateExceptionMessage"), StringComparison.OrdinalIgnoreCase);
        Assert.Null(Get(result, "UpdatedPluginId"));
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

    private static string? Get(object result, string propertyName)
        => (string?)result.GetType().GetProperty(propertyName)!.GetValue(result);

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample.Game
        {
            [RpcService]
            public interface IGameWorldAccess;
        }

        namespace Sample.Game.Ipc
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
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.Game.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");
            }
        }

        namespace Sample.Plugin
        {
            using Sample.Game;
            using Sample.Game.Ipc;

            public sealed record DamageEvent(string TargetId);

            [Plugin("guardian")]
            public sealed partial class GuardianKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int AggroRange { get; set; } = 5;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "ok");
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            public sealed class RecordingControl : IGamePluginControlService
            {
                public string? UpdatedPluginId { get; private set; }

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("wrong-id");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("wrong-id");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("wrong-id");

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                {
                    UpdatedPluginId = pluginId;
                    return ValueTask.CompletedTask;
                }

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
                    => ValueTask.CompletedTask;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }

            public sealed record ProbeResult(
                string? StartExceptionType,
                string? StartExceptionMessage,
                string? UpdateExceptionType,
                string? UpdateExceptionMessage,
                string? UpdatedPluginId);

            public static class InstallMismatchProbe
            {
                public static async Task<object> RunAsync()
                {
                    var control = new RecordingControl();
                    var server = new RemotePluginServer(
                        control,
                        null,
                        setup => setup.Replace<IEventKernel<DamageEvent>, GuardianKernel>());

                    var startException = await CaptureAsync(() => server.StartAsync().AsTask());
                    var updateException = await CaptureAsync(() =>
                        server.Get<GuardianKernel>().SetValuesAsync(kernel => kernel.AggroRange = 6).AsTask());

                    return new ProbeResult(
                        startException?.GetType().FullName,
                        startException?.Message,
                        updateException?.GetType().FullName,
                        updateException?.Message,
                        control.UpdatedPluginId);
                }

                private static async Task<Exception?> CaptureAsync(Func<Task> action)
                {
                    try
                    {
                        await action().ConfigureAwait(false);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }
            }
        }
        """;
}
