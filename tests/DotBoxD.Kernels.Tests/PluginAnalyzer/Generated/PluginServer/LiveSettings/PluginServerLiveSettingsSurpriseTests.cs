using System.Reflection;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsSurpriseTests
{
    [Fact]
    public async Task Generated_live_settings_handle_rejects_non_live_setting_writes_in_SetValuesAsync()
    {
        var assembly = Emit(TestSource);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, null])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("SetMixedValues", BindingFlags.Public | BindingFlags.Static)!;

        var exception = await Record.ExceptionAsync(
            async () => await AwaitValueTask(run.Invoke(null, [server])!));

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("ShadowDamage", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_live_settings_handle_allows_computed_non_live_values_in_SetValuesAsync()
    {
        var assembly = Emit(TestSource);
        var control = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [control, null])!;
        var run = assembly.GetType("Sample.Usage", throwOnError: true)!
            .GetMethod("SetLiveValue", BindingFlags.Public | BindingFlags.Static)!;

        var exception = await Record.ExceptionAsync(
            async () => await AwaitValueTask(run.Invoke(null, [server])!));

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("installed", invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EffectiveDamage", invalid.Message, StringComparison.Ordinal);
    }

    private static Assembly Emit(string source)
    {
        var (_, outputCompilation) = PluginServerGenerationTestDriver.Run(source);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
    }

    private const string TestSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample
        {
            [DotBoxDService]
            public interface IGameWorldAccess;

            public sealed record DamageEvent(string TargetId, int Amount);

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

            [GeneratePluginServer(
                Context = typeof(RemotePluginContext),
                ControlService = typeof(IGamePluginControlService))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;

            [Plugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 1;

                public int ShadowDamage { get; set; } = 2;

                public int EffectiveDamage => MinDamage + ShadowDamage;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => e.Amount >= MinDamage;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");
            }

            public static class Usage
            {
                public static ValueTask SetMixedValues(RemotePluginServer server)
                    => server.Get<FireDamageKernel>().SetValuesAsync(kernel =>
                    {
                        kernel.MinDamage = 5;
                        kernel.ShadowDamage = 99;
                    }, atomic: true);

                public static ValueTask SetLiveValue(RemotePluginServer server)
                    => server.Get<FireDamageKernel>().SetValuesAsync(kernel => kernel.MinDamage = 5);
            }

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default)
                    => ValueTask.FromResult("plugin-id");

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
                    => ValueTask.FromResult(System.Array.Empty<byte>());
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }
        """;
}
