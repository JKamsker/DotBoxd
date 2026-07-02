using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

// Verifies reverse server-to-plugin event callback wiring for generated plugin server facades.
public sealed class PluginServerRemoteLocalWiringTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_server_wires_remote_local_event_callback_when_world_declares_one()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Reactive.Game
            {
                [RpcService]
                public interface IGameWorldAccess;
            }

            namespace Reactive.Game.Ipc
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

                [RpcService]
                public interface IPluginEventCallback
                {
                    ValueTask OnEventAsync(string subscriptionId, System.ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);
                    ValueTask<byte[]> OnResultAsync(string subscriptionId, System.ReadOnlyMemory<byte> contextValue, CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static Reactive.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        Reactive.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace Reactive.Plugin
            {
                using DotBoxD.Abstractions;
                using Reactive.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Contains(
            "private readonly global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry _localHandlers = new();",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "new RemotePluginHookRegistry(package => InstallPluginPackageAsync(package), _localHandlers)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "public global::DotBoxD.Plugins.Runtime.RemoteHookPipeline<TEvent, global::Reactive.Plugin.RemotePluginContext> On<TEvent>()",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "=> _inner.On<TEvent, global::Reactive.Plugin.RemotePluginContext>(global::Reactive.Plugin.RemotePluginContext.FromHookContext);",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("setup(new SetupRecorder(installs, _localHandlers));", generated, StringComparison.Ordinal);
        Assert.Contains("public SetupRecorder(", generated, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Generic.List<RecordedInstall> installs,",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry? localHandlers)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("}, localHandlers);", generated, StringComparison.Ordinal);
        Assert.Contains(
            "new RemotePluginSubscriptionRegistry(package => InstallSubscriptionPackageAsync(package), _localHandlers)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvidePluginEventCallback(peer, new RemoteLocalEventSink(_localHandlers))",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "private sealed class RemoteLocalEventSink : global::Reactive.Game.Ipc.IPluginEventCallback",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "_localHandlers.DispatchAsync(subscriptionId, projectedValue, new global::DotBoxD.Abstractions.HookContext(",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_omits_local_handlers_when_world_declares_no_event_callback()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Plain.Game
            {
                [RpcService]
                public interface IGameWorldAccess;
            }

            namespace Plain.Game.Ipc
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
                    public static Plain.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Plain.Plugin
            {
                using DotBoxD.Abstractions;
                using Plain.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Contains(
            "new RemotePluginHookRegistry(package => InstallPluginPackageAsync(package));",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_localHandlers", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteLocalEventSink", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_omits_local_handlers_when_event_callback_return_type_is_not_value_task()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace WrongCallback.Game
            {
                [RpcService]
                public interface IGameWorldAccess;
            }

            namespace WrongCallback.Game.Ipc
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

                [RpcService]
                public interface IPluginEventCallback
                {
                    Task OnEventAsync(string subscriptionId, System.ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);
                    ValueTask<byte[]> OnResultAsync(string subscriptionId, System.ReadOnlyMemory<byte> contextValue, CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static WrongCallback.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        WrongCallback.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace WrongCallback.Plugin
            {
                using DotBoxD.Abstractions;
                using WrongCallback.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Contains(
            "new RemotePluginHookRegistry(package => InstallPluginPackageAsync(package));",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_localHandlers", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteLocalEventSink", generated, StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Result, Compilation OutputCompilation) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerRemoteLocalWiringTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return (driver.GetRunResult(), outputCompilation);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
