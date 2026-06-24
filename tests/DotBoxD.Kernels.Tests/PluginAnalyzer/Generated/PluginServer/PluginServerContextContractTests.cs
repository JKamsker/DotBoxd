using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RuntimePluginAnalyzer = DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerContextContractTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Missing_context_reports_a_generation_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer("""
            [GeneratePluginServer]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("must declare Context = typeof(TContext)", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_server_augments_author_declared_context_in_its_namespace()
    {
        var generated = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(MinimalServer("""
            [GeneratePluginServer(
                Context = typeof(Sample.Contexts.GameContext),
                ContextFactory = nameof(Sample.Contexts.GameContext.Create))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;
            """, """
            namespace Sample.Contexts
            {
                using DotBoxD.Abstractions;

                public sealed partial class GameContext
                {
                    public static GameContext Create(HookContext raw)
                        => throw new System.NotSupportedException("test factory");
                }
            }
            """));
        var source = string.Join("\n", generated);

        Assert.Contains("namespace Sample.Contexts", source, StringComparison.Ordinal);
        Assert.Contains("public partial class GameContext", source, StringComparison.Ordinal);
        Assert.Contains("FromHookContext(global::DotBoxD.Abstractions.HookContext raw) => Create(raw);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public global::Sample.Game.IGameWorld World => _raw.Host<global::Sample.Game.IGameWorld>();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RemoteHookPipeline<TEvent, global::Sample.Contexts.GameContext> On<TEvent>()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Context_host_binding_member_reports_a_generation_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                [HostBinding("host.context.read", "sample.read", SandboxEffect.Cpu)]
                public int Read => 0;
            }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("must not declare [HostBinding] members", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_context_use_reports_a_generation_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer("""
            [GeneratePluginServer(Context = typeof(Sample.Plugin.GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            [GeneratePluginServer(Context = typeof(Sample.Plugin.GameContext))]
            public partial class OtherPluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("Each generated server must declare its own context type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Local_attribute_outside_declared_context_reports_DBXK116()
    {
        var diagnostics = await AnalyzerDiagnosticsAsync("""
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class Helper
            {
                [Local]
                public string Native => "x";
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK116");
    }

    [Fact]
    public async Task Local_context_member_used_in_lowered_stage_reports_DBXK116()
    {
        var diagnostics = await AnalyzerDiagnosticsAsync(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                public GameContext(HookContext raw) { }

                [Local]
                public string NativeName => "local";
            }

            public sealed record Ping(string Id);

            public static class Usage
            {
                public static void Configure(DotBoxD.Plugins.Runtime.HookRegistry hooks)
                    => hooks.On<Ping, GameContext>(raw => new GameContext(raw))
                        .Where((e, ctx) => ctx.NativeName == "local")
                        .Run(e => { });
            }
            """));

        Assert.Contains(diagnostics, d => d.Id == "DBXK116");
    }

    [Fact]
    public async Task Local_context_member_used_in_server_extension_method_reports_DBXK116()
    {
        var diagnostics = await AnalyzerDiagnosticsAsync(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                [Local]
                public string NativeName => "local";
            }

            public static class ExtensionMethods
            {
                [ServerExtensionMethod]
                public static int Read(GameContext ctx) => ctx.NativeName.Length;
            }
            """));

        Assert.Contains(diagnostics, d => d.Id == "DBXK116");
    }

    private static string MinimalServer(string pluginSource, string extraSource = "")
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample.Game
            {
                [DotBoxDService]
                public interface IGameWorld;
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
                    public static Sample.Game.IGameWorld GetGameWorld(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace Sample.Plugin
            {
                {{pluginSource}}
            }

            {{extraSource}}
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzerDiagnosticsAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerContextContractTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RuntimePluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
