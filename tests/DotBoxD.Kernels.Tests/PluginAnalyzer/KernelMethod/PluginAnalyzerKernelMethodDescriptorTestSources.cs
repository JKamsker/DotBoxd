namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

internal static class PluginAnalyzerKernelMethodDescriptorTestSources
{
    public const string Sdk = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace Sdk
        {
            [DotBoxDService]
            public interface IGameWorld
            {
                [HostCapability("sample.read.value", HostBindingEffect.HostStateRead)]
                int Read(string id);
            }

            [GeneratePluginServer(Context = typeof(GamePluginContext))]
            public partial class GamePluginServer : IGameWorld;

            public sealed partial class GamePluginContext
            {
                [KernelMethod]
                public bool IsAllowed(string id, int threshold) => World.Read(id) >= threshold;

                [KernelMethod]
                public bool IsClose(int distance) => distance <= 5;
            }
        }

        namespace Sdk.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sdk.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }
        """;

    public const string Consumer = """
        using DotBoxD.Plugins;
        using Sdk;

        namespace Consumer;

        public static class Usage
        {
            public static void Configure(GamePluginServer server)
                => server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Where((e, ctx) => ctx.IsAllowed(e.MonsterId, e.PlayerLevel) && ctx.IsClose(e.Distance))
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
        }
        """;

    public const string DescriptorlessSdk = """
        using DotBoxD.Abstractions;

        namespace Descriptorless;

        public sealed class GamePluginContext
        {
            private readonly HookContext _raw;
            public GamePluginContext(HookContext raw) => _raw = raw;
            public HookContext Raw => _raw;
            public IPluginMessageSink Messages => _raw.Messages;
            public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

            [KernelMethod]
            public bool IsClose(int distance) => distance <= 5;
        }
        """;

    public const string DescriptorlessConsumer = """
        using Descriptorless;
        using DotBoxD.Plugins.Runtime;

        namespace Consumer;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent, GamePluginContext>(GamePluginContext.FromHookContext)
                    .Where((e, ctx) => ctx.IsClose(e.Distance))
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
        }
        """;
}
