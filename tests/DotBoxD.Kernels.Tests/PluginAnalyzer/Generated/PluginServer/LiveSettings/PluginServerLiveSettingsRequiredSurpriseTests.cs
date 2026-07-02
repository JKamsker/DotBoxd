using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsRequiredSurpriseTests
{
    [Fact]
    public async Task Generated_live_settings_handle_rejects_missing_required_live_setting_in_SetValuesAsync()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [wire, null])!;
        var kernelType = assembly.GetType("Sample.Plugin.FireDamageKernel", throwOnError: true)!;

        var handle = serverType.GetMethod("Get")!
            .MakeGenericMethod(kernelType)
            .Invoke(server, null)!;
        var setValuesAsync = handle.GetType().GetMethod("SetValuesAsync")!;
        var action = CreateSetMinDamageOnlyAction(kernelType);

        var exception = await CaptureExceptionAsync(async () =>
        {
            var result = setValuesAsync.Invoke(handle, [action, false])!;
            await AwaitValueTask(result);
        });

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("DamageType", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_live_settings_handle_allows_required_value_type_default_in_SetValuesAsync()
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(Source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [wire, null])!;
        var kernelType = assembly.GetType("Sample.Plugin.FireDamageKernel", throwOnError: true)!;

        var handle = serverType.GetMethod("Get")!
            .MakeGenericMethod(kernelType)
            .Invoke(server, null)!;
        var setValuesAsync = handle.GetType().GetMethod("SetValuesAsync")!;
        var action = CreateSetRequiredDefaultsAction(kernelType);

        var exception = await CaptureExceptionAsync(async () =>
        {
            var result = setValuesAsync.Invoke(handle, [action, false])!;
            await AwaitValueTask(result);
        });

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("installed", invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsEnabled", invalid.Message, StringComparison.Ordinal);
    }

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
            using DotBoxD.Abstractions;
            using Sample.Game;

            public sealed record DamageEvent(string TargetId);

            [Plugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public required string DamageType { get; set; }

                [LiveSetting]
                public required bool IsEnabled { get; set; }

                [LiveSetting]
                public int MinDamage { get; set; }

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                {
                    ctx.Messages.Send(e.TargetId, DamageType);
                }
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace Sample
        {
            using Sample.Game.Ipc;

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public LiveSettingUpdate[]? LastUpdates { get; private set; }

                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("fire-damage");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("fire-damage");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("fire-damage");

                public ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default)
                {
                    LastUpdates = updates;
                    return default;
                }

                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => default;

                public ValueTask<byte[]> InvokeServerExtensionAsync(
                    string pluginId,
                    byte[] arguments,
                    CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(Array.Empty<byte>());
            }
        }
        """;

    private static Assembly Load(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static Delegate CreateSetMinDamageOnlyAction(Type kernelType)
    {
        var actionType = typeof(Action<>).MakeGenericType(kernelType);
        var method = typeof(PluginServerLiveSettingsRequiredSurpriseTests)
            .GetMethod(nameof(SetMinDamageOnly), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(kernelType);
        return Delegate.CreateDelegate(actionType, method);
    }

    private static Delegate CreateSetRequiredDefaultsAction(Type kernelType)
    {
        var actionType = typeof(Action<>).MakeGenericType(kernelType);
        var method = typeof(PluginServerLiveSettingsRequiredSurpriseTests)
            .GetMethod(nameof(SetRequiredDefaults), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(kernelType);
        return Delegate.CreateDelegate(actionType, method);
    }

    private static void SetMinDamageOnly<TKernel>(TKernel kernel)
        where TKernel : class
    {
        typeof(TKernel).GetProperty("MinDamage", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(kernel, 250);
    }

    private static void SetRequiredDefaults<TKernel>(TKernel kernel)
        where TKernel : class
    {
        typeof(TKernel).GetProperty("DamageType", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(kernel, "fire");
        typeof(TKernel).GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(kernel, false);
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        await (Task)asTask.Invoke(valueTask, null)!;
    }
}
