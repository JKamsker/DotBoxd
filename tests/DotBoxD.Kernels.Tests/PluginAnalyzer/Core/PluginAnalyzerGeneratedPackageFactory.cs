using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

internal static class PluginAnalyzerGeneratedPackageFactory
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static PluginPackage Create(
        string source,
        string factoryTypeName = "Sample.DamagePluginPackage",
        params Type[] additionalReferenceTypes)
    {
        var loaded = CreateAssembly(source, additionalReferenceTypes);
        var factory = loaded.GetType(factoryTypeName, throwOnError: true)!;
        var create = factory.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        return Assert.IsType<PluginPackage>(create.Invoke(null, null));
    }

    public static Assembly CreateAssembly(string source, params Type[] additionalReferenceTypes)
    {
        var compilation = CreateCompilation(source, additionalReferenceTypes);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        using var assembly = new MemoryStream();
        var emit = outputCompilation.Emit(assembly);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));

        return Assembly.Load(assembly.ToArray());
    }

    public static IReadOnlyList<Diagnostic> Diagnostics(string source, params Type[] additionalReferenceTypes)
    {
        var compilation = CreateCompilation(source, additionalReferenceTypes);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        return generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics().Where(
                d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            .ToArray();
    }

    private static CSharpCompilation CreateCompilation(string source, params Type[] additionalReferenceTypes)
        => CSharpCompilation.Create(
            "DotBoxDGeneratedPackageRuntimeTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Concat(AdditionalReferences(additionalReferenceTypes)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> AdditionalReferences(IEnumerable<Type> referenceTypes)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in referenceTypes)
        {
            var path = type.Assembly.Location;
            if (!string.IsNullOrWhiteSpace(path) && paths.Add(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
