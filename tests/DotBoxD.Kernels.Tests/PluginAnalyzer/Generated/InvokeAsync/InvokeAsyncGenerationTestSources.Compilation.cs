using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

internal static partial class InvokeAsyncGenerationTestSources
{
    internal static GeneratorDriverRunResult RunGeneratorAndAssertCompiles(string source)
    {
        var parseOptions = ParseOptions.WithFeatures(
            [new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDInvokeAsyncGeneratedCompileTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            CompileReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        using var assembly = new MemoryStream();
        var emit = outputCompilation.Emit(assembly);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> CompileReferences()
        => TrustedPlatformReferences().Where(static reference =>
        {
            var fileName = Path.GetFileName(reference.Display ?? string.Empty);
            return !fileName.StartsWith("Examples.GameServer.", StringComparison.Ordinal) &&
                   !string.Equals(fileName, "DotBoxD.Kernels.Tests.dll", StringComparison.Ordinal);
        });
}
