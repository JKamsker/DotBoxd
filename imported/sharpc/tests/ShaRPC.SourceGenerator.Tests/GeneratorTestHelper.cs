using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Attributes;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Helpers for constructing in-memory compilations and driving the ShaRPC generator
/// with tracking enabled so incrementality assertions can inspect tracked step outputs.
/// </summary>
internal static class GeneratorTestHelper
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    // System.Reflection.DispatchProxy caches its generated proxies by interface type, and
    // the type identity is partly the defining assembly name. When several in-memory test
    // compilations all share the name "compilation", the cache returns a proxy that was
    // emitted against a different (now-unloaded) interface, so reuse fails with a
    // TypeLoadException. Suffixing with an incrementing counter avoids that collision.
    private static int s_compilationCounter;

    /// <summary>
    /// Builds a compilation that contains the supplied user source plus references to
    /// the .NET BCL and the ShaRPC.Core marker attribute assembly. Each call uses a unique
    /// assembly name so that dynamically loaded test compilations do not collide in the
    /// process-wide caches used by reflection-based facilities such as DispatchProxy.
    /// </summary>
    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s, s_parseOptions)).ToArray();

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net80.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ShaRpcServiceAttribute).Assembly.Location),
            CreatePipelinesReference(),
        };

        var unique = Interlocked.Increment(ref s_compilationCounter);
        return CSharpCompilation.Create(
            assemblyName: $"GenTest_{unique}_{Guid.NewGuid():N}",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference CreatePipelinesReference()
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var packagePath = Path.Combine(
            packageRoot,
            "system.io.pipelines",
            "8.0.0",
            "lib",
            "netstandard2.0",
            "System.IO.Pipelines.dll");
        return MetadataReference.CreateFromFile(
            File.Exists(packagePath) ? packagePath : typeof(Pipe).Assembly.Location);
    }

    /// <summary>
    /// Returns a fresh driver with incremental step tracking enabled.
    /// </summary>
    public static GeneratorDriver CreateDriver()
    {
        var generator = new ShaRpcGenerator().AsSourceGenerator();
        return CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
    }

    /// <summary>
    /// Convenience: build, run, return (driver, compilation).
    /// </summary>
    public static (GeneratorDriver Driver, Compilation Compilation) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CreateDriver().RunGenerators(compilation);
        return (driver, compilation);
    }

    public enum GeneratedKind
    {
        Proxy,
        Dispatcher,
    }

    /// <summary>
    /// Reconstructs the hint name the generator produces for a (namespace, interface, kind)
    /// tuple. Mirrors <see cref="ShaRPC.SourceGenerator.ShaRpcGenerator.HintNamePrefix"/>:
    /// the namespace (with dots replaced by underscores) is prepended to the interface
    /// name so two services with the same simple name don't collide on <see cref="SourceProductionContext.AddSource"/>.
    /// Use this in tests instead of hardcoded literals so a future naming-scheme change
    /// only requires one edit.
    /// </summary>
    public static string HintName(string @namespace, string interfaceName, GeneratedKind kind)
    {
        var prefix = string.IsNullOrEmpty(@namespace)
            ? GlobalPrefix(interfaceName)
            : NamespaceIdentifierPrefix(@namespace) + "_" + interfaceName;
        var suffix = kind == GeneratedKind.Proxy ? "ShaRpcProxy" : "ShaRpcDispatcher";
        return $"{prefix}.{suffix}.g.cs";
    }

    private static string GlobalPrefix(string interfaceName) =>
        interfaceName.IndexOf('_') >= 0
            ? "Global-" + interfaceName
            : interfaceName;

    private static string NamespaceIdentifierPrefix(string namespaceName)
    {
        var normalized = namespaceName.Replace("@", "");
        var flattened = normalized.Replace('.', '_');
        if (!normalized.Contains('_'))
        {
            return flattened;
        }

        return flattened + "__" + StableHash(normalized);
    }

    private static string StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211;
            }

            return hash.ToString("x16");
        }
    }
}
