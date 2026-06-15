using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Detection;

/// <summary>
/// Phase C-0 (detection only): the analyzer flags an inline Run(lambda) hook-chain terminal
/// with informational DBXK110, since lowering those lambdas to verified IR is a later phase and the
/// runtime terminal throws until then.
/// </summary>
public sealed class HookChainDetectionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_DBXK110_on_an_inline_Run_lambda_chain()
    {
        var diagnostics = await AnalyzeAsync("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample
            {
                public sealed record DamageEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<DamageEvent>().Run((e, ctx) => default);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "DBXK110");
    }

    [Fact]
    public async Task Does_not_report_DBXK110_for_RunLocal()
    {
        var diagnostics = await AnalyzeAsync("""
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

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainDetectionTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
