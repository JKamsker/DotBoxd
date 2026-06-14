using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DotBoxd.Services.Attributes;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class SubServicePayloadCacheTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ExternAliasTypesWithSameDisplayName_DoNotSharePayloadCacheEntries()
    {
        var cleanReference = CompileReference("""
            namespace Shared
            {
                public sealed class Request
                {
                    public int Id { get; init; }
                }
            }
            """, "Clean");
        var dirtyReference = CompileReference("""
            #nullable enable
            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Shared
            {
                [DotBoxdService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                public sealed class Request
                {
                    public ISub? Sub { get; init; }
                }
            }
            """, "Dirty");
        var compilation = CreateCompilation("""
            extern alias Clean;
            extern alias Dirty;

            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ExternAliasPayloadCache
            {
                [DotBoxdService]
                public interface IRoot
                {
                    Task CleanAsync(Clean::Shared.Request request);
                    Task DirtyAsync(Dirty::Shared.Request request);
                }
            }
            """, cleanReference, dirtyReference);

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("DirtyAsync") &&
            d.GetMessage().Contains("contains a sub-service type"));

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxdRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"CleanAsync\":");
        dispatcher.Should().NotContain("case \"DirtyAsync\":");
    }

    private static MetadataReference CompileReference(string source, string alias)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "AliasRef_" + alias + "_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: CreateBaseReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(ms.ToArray())
            .WithAliases(ImmutableArray.Create(alias));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        params MetadataReference[] aliasedReferences) =>
        CSharpCompilation.Create(
            assemblyName: "ExternAliasPayloadCache_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, s_parseOptions) },
            references: CreateBaseReferences().Concat(aliasedReferences),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> CreateBaseReferences()
    {
        foreach (var reference in Basic.Reference.Assemblies.Net80.References.All)
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(DotBoxdServiceAttribute).Assembly.Location);
    }
}
