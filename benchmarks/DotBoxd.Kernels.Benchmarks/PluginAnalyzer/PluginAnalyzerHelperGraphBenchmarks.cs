namespace DotBoxd.Kernels.Benchmarks.PluginAnalyzer;

using System.Collections.Immutable;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using DotBoxd.Plugins.Analyzer;
using DotBoxd.Plugins;

[MemoryDiagnoser]
public class PluginAnalyzerHelperGraphBenchmarks
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private CSharpCompilation _compilation = null!;
    private ImmutableArray<DiagnosticAnalyzer> _analyzers;

    [Params(100, 1_000, 10_000)]
    public int HelperCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _compilation = CreateCompilation(BuildSource(HelperCount));
        _analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxdPluginAnalyzer());
    }

    [Benchmark]
    public ImmutableArray<Diagnostic> AnalyzeHelperChain()
        => _compilation
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();

    private static string BuildSource(int helperCount)
    {
        var source = new StringBuilder();
        source.AppendLine("using DotBoxd.Plugins;");
        source.AppendLine("public static class BadHelper");
        source.AppendLine("{");
        for (var i = 0; i < helperCount; i++) {
            source.Append("    public static void Step");
            source.Append(i);
            source.Append("() => ");
            if (i + 1 == helperCount) {
                source.AppendLine("System.IO.File.WriteAllText(\"x.txt\", \"bad\");");
            }
            else {
                source.Append("Step");
                source.Append(i + 1);
                source.AppendLine("();");
            }
        }

        source.AppendLine("}");
        source.AppendLine("[Plugin(\"bad\")]");
        source.AppendLine("public sealed class BadKernel : IEventKernel<string>");
        source.AppendLine("{");
        source.AppendLine("    public bool ShouldHandle(string e, HookContext context)");
        source.AppendLine("    {");
        source.AppendLine("        BadHelper.Step0();");
        source.AppendLine("        return true;");
        source.AppendLine("    }");
        source.AppendLine("    public void Handle(string e, HookContext context) { }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxdPluginAnalyzerBenchmark",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
