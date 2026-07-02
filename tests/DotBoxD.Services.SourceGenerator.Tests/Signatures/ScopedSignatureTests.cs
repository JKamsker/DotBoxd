using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class ScopedSignatureTests
{
    [Fact]
    public void ScopedParameters_ArePreservedInProxyStubSignatures()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;

            namespace Regress.ScopedSignatures
            {
                [DotBoxDService]
                public interface IScopedParameters
                {
                    void Inspect(scoped ReadOnlySpan<byte> value);
                    void InspectIn(scoped in int value);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002")
            .Should().HaveCount(2);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IScopedParameters.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public void Inspect(scoped global::System.ReadOnlySpan<byte> value)");
        proxy.Should().Contain("public void InspectIn(scoped in int value)");

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
