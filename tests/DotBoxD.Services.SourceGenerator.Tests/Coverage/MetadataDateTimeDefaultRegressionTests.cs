using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// Regression for inherited metadata-only optional defaults. A referenced base interface can legally
/// expose an optional DateTime parameter via DateTimeConstantAttribute; the generated proxy and async
/// sibling should preserve that default instead of silently making the parameter required.
/// </summary>
public sealed class MetadataDateTimeDefaultRegressionTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void InheritedMetadataDateTimeDefault_IsPreservedOnGeneratedProxyAndAsyncSibling()
    {
        var reference = CompileReference("""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace MetadataDefaults
            {
                public interface IBaseSchedule
                {
                    void Ping([Optional, DateTimeConstant(0L)] DateTime when);
                }
            }
            """);
        var compilation = CreateCompilation("""
            using DotBoxD.Services.Attributes;

            namespace Regress.MetadataDefaults
            {
                [DotBoxDService]
                public interface ISchedule : MetadataDefaults.IBaseSchedule
                {
                }
            }
            """, reference);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.MetadataDefaults",
                "ISchedule",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("Ping(global::System.DateTime when = default)");

        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("ISchedule.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();
        asyncSibling.Should().Contain(
            "PingAsync(global::System.DateTime when = default, global::System.Threading.CancellationToken ct = default)");

        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }

    private static MetadataReference CompileReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "MetadataDefaultsRef_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string source, MetadataReference reference) =>
        CSharpCompilation.Create(
            assemblyName: "MetadataDefaultsTest_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences().Append(reference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> CreateBaseReferences()
    {
        foreach (var reference in Basic.Reference.Assemblies.Net80.References.All)
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(DotBoxDServiceAttribute).Assembly.Location);
    }
}
