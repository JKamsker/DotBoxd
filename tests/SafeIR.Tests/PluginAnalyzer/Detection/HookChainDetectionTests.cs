using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Phase C-0 (detection only): the analyzer flags an inline InvokeKernel(lambda) hook-chain terminal
/// with informational SGP110, since lowering those lambdas to verified IR is a later phase and the
/// runtime terminal throws until then.
/// </summary>
public sealed class HookChainDetectionTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_SGP110_on_an_inline_InvokeKernel_lambda_chain()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample
            {
                public sealed record DamageEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<DamageEvent>().InvokeKernel((e, ctx) => default);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP110");
    }

    [Fact]
    public async Task Does_not_report_SGP110_for_InvokeLocal()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample
            {
                public sealed record DamageEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<DamageEvent>().InvokeLocal((e, ctx) => default);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SGP110");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrHookChainDetectionTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SafeIrPluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
