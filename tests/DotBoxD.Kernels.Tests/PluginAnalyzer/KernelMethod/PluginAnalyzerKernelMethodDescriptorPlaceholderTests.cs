using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_repeated_nonrepeatable_argument_placeholder()
    {
        var sdkReference = CompilePlainReference(
            RepeatedPlaceholderDescriptorSdkSource(),
            "RepeatedPlaceholderDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(RepeatedPlaceholderConsumerSource(), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("used more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_repeated_placeholder_inside_string_equality_helper()
    {
        var sdkReference = CompilePlainReference(
            RepeatedStringPlaceholderDescriptorSdkSource(),
            "RepeatedStringPlaceholderDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(RepeatedStringPlaceholderConsumerSource(), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("used more than once", StringComparison.Ordinal));
    }

    private static string RepeatedPlaceholderConsumerSource()
        => """
            using DotBoxD.Plugins.Runtime;
            using Sdk;

            namespace Consumer;

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent, GamePluginContext>(GamePluginContext.FromHookContext)
                        .Where((e, ctx) => ctx.IsLarge(ctx.World.Read(e.MonsterId)))
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """;

    private static string RepeatedPlaceholderDescriptorSdkSource()
    {
        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            "global::Sdk.GamePluginContext",
            "IsLarge",
            "bool IsLarge(int)",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false,
            new EquatableArray<string>([]),
            new EquatableArray<string>([]),
            new EquatableArray<KernelMethodDescriptorParameter>(
            [
                new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.Int)
            ]),
            "Gt(__dotboxd_kernel_method_arg_0__, __dotboxd_kernel_method_arg_0__)");
        var json = payload.ToJson();
        var hash = KernelMethodDescriptorPayload.Hash(json);
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            [assembly: global::DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute(
                {{KernelMethodDescriptorPayload.CurrentVersion}},
                typeof(global::Sdk.GamePluginContext),
                "IsLarge",
                "bool IsLarge(int)",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace Sdk
            {
                [DotBoxDService]
                public interface IGameWorld
                {
                    [HostCapability("sample.read.value", HostBindingEffect.HostStateRead)]
                    int Read(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class GamePluginServer : IGameWorld
                {
                }

                public sealed class GamePluginContext
                {
                    private readonly HookContext _raw;
                    public GamePluginContext(HookContext raw) => _raw = raw;
                    public HookContext Raw => _raw;
                    public IPluginMessageSink Messages => _raw.Messages;
                    public IGameWorld World => _raw.Host<IGameWorld>();
                    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

                    [KernelMethod]
                    public bool IsLarge(int value)
                        => throw new System.NotSupportedException("metadata-only descriptor");
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
    }

    private static string RepeatedStringPlaceholderConsumerSource()
        => """
            using DotBoxD.Plugins.Runtime;
            using Sdk;

            namespace Consumer;

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent, GamePluginContext>(GamePluginContext.FromHookContext)
                        .Where((e, ctx) => ctx.IsKnown(ctx.World.ReadName(e.MonsterId)))
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """;

    private static string RepeatedStringPlaceholderDescriptorSdkSource()
    {
        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            "global::Sdk.GamePluginContext",
            "IsKnown",
            "bool IsKnown(string)",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false,
            new EquatableArray<string>([]),
            new EquatableArray<string>([]),
            new EquatableArray<KernelMethodDescriptorParameter>(
            [
                new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
            ]),
            "StringEquals(__dotboxd_kernel_method_arg_0__, __dotboxd_kernel_method_arg_0__)");
        var json = payload.ToJson();
        var hash = KernelMethodDescriptorPayload.Hash(json);
        return $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Services.Attributes;

            [assembly: global::DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute(
                {{KernelMethodDescriptorPayload.CurrentVersion}},
                typeof(global::Sdk.GamePluginContext),
                "IsKnown",
                "bool IsKnown(string)",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace Sdk
            {
                [DotBoxDService]
                public interface IGameWorld
                {
                    [HostCapability("sample.read.value", HostBindingEffect.HostStateRead | HostBindingEffect.Allocates)]
                    string ReadName(string id) => throw new System.NotSupportedException("metadata-only world");
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class GamePluginServer : IGameWorld
                {
                }

                public sealed class GamePluginContext
                {
                    private readonly HookContext _raw;
                    public GamePluginContext(HookContext raw) => _raw = raw;
                    public HookContext Raw => _raw;
                    public IPluginMessageSink Messages => _raw.Messages;
                    public IGameWorld World => _raw.Host<IGameWorld>();
                    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

                    [KernelMethod]
                    public bool IsKnown(string value)
                        => throw new System.NotSupportedException("metadata-only descriptor");
                }
            }
            """;
    }
}
