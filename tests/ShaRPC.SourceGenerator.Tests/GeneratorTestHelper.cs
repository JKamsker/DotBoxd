using System;
using System.Collections.Generic;
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
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net80.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ShaRpcServiceAttribute).Assembly.Location),
        };

        var unique = Interlocked.Increment(ref s_compilationCounter);
        return CSharpCompilation.Create(
            assemblyName: $"GenTest_{unique}_{Guid.NewGuid():N}",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
}
