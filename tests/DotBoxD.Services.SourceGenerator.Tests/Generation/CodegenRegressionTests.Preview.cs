using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    private static (Compilation Final, GeneratorDriverRunResult RunResult) RunWithPreviewByRefLikeGenerics(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName: $"GenPreviewTest_{Guid.NewGuid():N}",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    "[assembly: System.Runtime.Versioning.TargetFramework(\".NETCoreApp,Version=v10.0\", FrameworkDisplayName = \".NET 10.0\")]",
                    parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions),
            ],
            references: Net10ReferenceAssemblies()
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = GeneratorTestHelper.CreateDriver(parseOptions).RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        return (finalCompilation, runResult);
    }

    private static IEnumerable<MetadataReference> Net10ReferenceAssemblies()
    {
        var dotnetRoot = FindDotnetRoot();
        var packsRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var referenceDirectory = Directory.EnumerateDirectories(packsRoot)
            .Select(static directory => new
            {
                Directory = Path.Combine(directory, "ref", "net10.0"),
                Version = Version.TryParse(Path.GetFileName(directory), out var version) ? version : null,
            })
            .Where(static candidate => candidate.Version is { Major: 10 } &&
                Directory.Exists(candidate.Directory))
            .OrderByDescending(static candidate => candidate.Version)
            .Select(static candidate => candidate.Directory)
            .FirstOrDefault();

        if (referenceDirectory is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not find the .NET 10 reference pack under '{packsRoot}'.");
        }

        return Directory.EnumerateFiles(referenceDirectory, "*.dll")
            .Select(static reference => MetadataReference.CreateFromFile(reference));
    }

    private static string FindDotnetRoot()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root) && HasNet10ReferencePack(root))
        {
            return root;
        }

        var runtimeDirectory = Directory.GetParent(typeof(object).Assembly.Location);
        var appDirectory = runtimeDirectory?.Parent;
        var sharedDirectory = appDirectory?.Parent;
        var dotnetDirectory = sharedDirectory?.Parent;
        if (dotnetDirectory is not null && HasNet10ReferencePack(dotnetDirectory.FullName))
        {
            return dotnetDirectory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the dotnet installation root.");
    }

    private static bool HasNet10ReferencePack(string dotnetRoot)
        => Directory.Exists(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref")) &&
           Directory.EnumerateDirectories(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
               .Any(static directory =>
                   Version.TryParse(Path.GetFileName(directory), out var version) &&
                   version.Major == 10 &&
                   Directory.Exists(Path.Combine(directory, "ref", "net10.0")));
}
