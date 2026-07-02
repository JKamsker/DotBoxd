using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public class NullableSignatureTests
{
    [Fact]
    public void NullableReferenceAnnotations_ArePreservedInProxyAndAsyncSiblingSignatures()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableSignatures
            {
                [RpcService]
                public interface INulls
                {
                    Task<string?> EchoAsync(string? value);
                    string? Read(string? key);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("INulls.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("global::System.Threading.Tasks.Task<string?> EchoAsync(string? value)");
        proxy.Should().Contain("string? Read(string? key)");
        proxy.Should().Contain("global::System.Threading.Tasks.Task<string?> ReadAsync(");
        proxy.Should().Contain("string? key, global::System.Threading.CancellationToken ct = default");

        var asyncInterface = generated
            .Single(g => g.HintName.EndsWith("INulls.DotBoxDRpcAsync.g.cs"))
            .SourceText.ToString();
        asyncInterface.Should().Contain("global::System.Threading.Tasks.Task<string?> EchoAsync(");
        asyncInterface.Should().Contain("string? value, global::System.Threading.CancellationToken ct = default");
        asyncInterface.Should().Contain("global::System.Threading.Tasks.Task<string?> ReadAsync(");
        asyncInterface.Should().Contain("string? key, global::System.Threading.CancellationToken ct = default");
    }

    [Fact]
    public void ReturnFlowAttributes_ArePreservedOnProxyMethods()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;

            namespace Regress.NullableSignatures
            {
                [RpcService]
                public interface INulls
                {
                    [return: NotNullIfNotNull(nameof(value))]
                    string? Echo(string? value);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("INulls.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(
            "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"value\")]");
        proxy.Should().Contain("string? Echo(string? value)");

        var asyncInterface = generated
            .Single(g => g.HintName.EndsWith("INulls.DotBoxDRpcAsync.g.cs"))
            .SourceText.ToString();
        asyncInterface.Should().NotContain("NotNullIfNotNullAttribute");
    }

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    private static void AssertCompiles(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
