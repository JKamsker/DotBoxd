using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

internal static class PluginServerGenerationTestDriver
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static (string Generated, Compilation OutputCompilation) Run(string source)
        => Run(source, includePushdownServices: true);

    public static (string Generated, Compilation OutputCompilation) RunWithoutPushdownServices(string source)
        => Run(source, includePushdownServices: false);

    private static (string Generated, Compilation OutputCompilation) Run(
        string source,
        bool includePushdownServices)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerRegressionTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences(includePushdownServices)
                .Append(MetadataReference.CreateFromFile(typeof(GeneratePluginServerAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(IsError));
        var generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString()));
        return (generated, outputCompilation);
    }

    public static IReadOnlyList<Diagnostic> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginServerDiagnosticTest",
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

        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    public static void AssertNoCompilationErrors(Compilation compilation)
    {
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
    }

    private static bool IsError(Diagnostic diagnostic)
    {
        return diagnostic.Severity == DiagnosticSeverity.Error;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences(bool includePushdownServices = true)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references
            .Where(reference => includePushdownServices ||
                                !string.Equals(
                                    Path.GetFileNameWithoutExtension(reference),
                                    "DotBoxD.Pushdown.Services",
                                    StringComparison.Ordinal))
            .Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
