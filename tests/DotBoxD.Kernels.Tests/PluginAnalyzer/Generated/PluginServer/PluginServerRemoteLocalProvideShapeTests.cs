using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerRemoteLocalProvideShapeTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_server_omits_local_handlers_when_result_callback_method_is_missing()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace MissingResult.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace MissingResult.Game.Ipc
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

                [DotBoxDService]
                public interface IPluginEventCallback
                {
                    ValueTask OnEventAsync(string subscriptionId, System.ReadOnlyMemory<byte> projectedValue, CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static MissingResult.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        MissingResult.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace MissingResult.Plugin
            {
                using DotBoxD.Abstractions;
                using MissingResult.Game;

                [GeneratePluginServer]
                public partial class RemotePluginServer : IGameWorldAccess;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "new global::DotBoxD.Plugins.Runtime.RemoteHookRegistry(package => InstallPluginPackageAsync(package));",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_localHandlers", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteLocalEventSink", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_omits_local_handlers_when_provide_callback_overload_has_wrong_shape()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace WrongProvide.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess;
            }

            namespace WrongProvide.Game.Ipc
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

                [DotBoxDService]
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
                    public static WrongProvide.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => peer;
                }
            }

            namespace WrongProvide.Plugin
            {
                using DotBoxD.Abstractions;
                using WrongProvide.Game;

                [GeneratePluginServer]
                public partial class RemotePluginServer : IGameWorldAccess;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "new global::DotBoxD.Plugins.Runtime.RemoteHookRegistry(package => InstallPluginPackageAsync(package));",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_localHandlers", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteLocalEventSink", generated, StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Result, Compilation OutputCompilation) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerRemoteLocalProvideShapeTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return (driver.GetRunResult(), outputCompilation);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
