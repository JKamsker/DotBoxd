using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DiagnosticSeverity = DotBoxD.Kernels.Model.DiagnosticSeverity;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerDocumentationTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_server_carries_intellisense_documentation()
    {
        var (result, outputCompilation) = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                /// <summary>Domain docs for the game world.</summary>
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    /// <summary>Domain docs for monster commands.</summary>
                    IMonsterControl Monsters { get; }
                }

                [DotBoxDService]
                public interface IMonsterControl
                {
                    /// <summary>Domain docs for scoped monster handles.</summary>
                    IMonster Get(string entityId);

                    /// <summary>Domain docs for monster classification.</summary>
                    ValueTask<bool> IsMonsterAsync(string entityId);
                }

                /// <summary>Domain docs for the monster handle type.</summary>
                [DotBoxDService]
                public interface IMonster
                {
                    /// <summary>Domain docs for the monster handle id.</summary>
                    string Id { get; }

                    /// <summary>Domain docs for killing a monster.</summary>
                    ValueTask<bool> KillAsync();
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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
                    public static DotBoxD.Kernels.Game.Server.Abstractions.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Kernels.Game.Server.Abstractions;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        Assert.Contains("Generated plugin-side client for the remote world domain.", generated, StringComparison.Ordinal);
        Assert.Contains("Remote hook registration surface.", generated, StringComparison.Ordinal);
        Assert.Contains("Remote fire-and-forget subscription registration surface.", generated, StringComparison.Ordinal);
        Assert.Contains("Domain docs for the game world.", generated, StringComparison.Ordinal);
        Assert.Contains("Domain docs for monster commands.", generated, StringComparison.Ordinal);
        Assert.Contains("Domain docs for scoped monster handles.", generated, StringComparison.Ordinal);
        Assert.Contains("Domain docs for the monster handle type.", generated, StringComparison.Ordinal);
        Assert.Contains("Domain docs for killing a monster.", generated, StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Result, Compilation OutputCompilation) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerDocumentationTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity.Equals(DiagnosticSeverity.Error)));
        return (driver.GetRunResult(), outputCompilation);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
