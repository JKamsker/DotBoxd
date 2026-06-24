using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PluginPackageGenerator = DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator;
using PluginServer = DotBoxD.Plugins.PluginServer;
using RuntimePluginAnalyzer = DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Detection;

/// <summary>
/// DBXK110 used to come from the analyzer without knowing whether the generator lowered the chain. The generator
/// now owns not-lowered hook-chain diagnostics, so lowered chains must not carry DBXK110.
/// </summary>
public sealed class HookChainDetectionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Lowered_Run_lambda_chain_reports_no_DBXK110_when_generator_runs()
    {
        var result = await AnalyzeWithGeneratorAsync("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample
            {
                public sealed record DamageEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<DamageEvent>().Run((e, ctx) => ctx.Messages.Send(e.TargetId, "damage"));
                }
            }
            """);

        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK110");
    }

    [Fact]
    public void Plugin_analyzer_no_longer_advertises_DBXK110()
        => Assert.DoesNotContain(
            new RuntimePluginAnalyzer().SupportedDiagnostics,
            descriptor => descriptor.Id == "DBXK110");

    [Fact]
    public async Task RunLocal_reports_no_DBXK110()
    {
        var diagnostics = await AnalyzeOnlyAsync("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample
            {
                public sealed record DamageEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<DamageEvent>().RunLocal((e, ctx) => default);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK110");
    }

    private static async Task<(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<SyntaxTree> GeneratedTrees)>
        AnalyzeWithGeneratorAsync(string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();
        var diagnostics = result.Diagnostics.AddRange(await AnalyzeCompilationAsync(output));
        return (diagnostics, result.GeneratedTrees);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeOnlyAsync(string source)
        => await AnalyzeCompilationAsync(CreateCompilation(source));

    private static Compilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDHookChainDetectionTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeCompilationAsync(Compilation compilation)
    {
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
