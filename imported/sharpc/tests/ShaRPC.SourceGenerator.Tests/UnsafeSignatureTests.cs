using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class UnsafeSignatureTests
{
    [Fact]
    public void UnsafeUnsupportedMethods_EmitUnsafeProxyStubs_AndCompile()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsafeShapes
            {
                [ShaRpcService]
                public unsafe interface IUnsafeRpc
                {
                    void PointerParameter(int* value);
                    int* PointerReturn();
                    void FunctionPointerParameter(delegate*<void> callback);
                    delegate*<int> FunctionPointerReturn();
                    Task<int> GoodAsync();
                }
            }
            """;

        var compilation = WithUnsafe(GeneratorTestHelper.CreateCompilation(source));
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC002")
            .Should().HaveCount(4);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IUnsafeRpc.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public unsafe void PointerParameter(int* value)");
        proxy.Should().Contain("public unsafe int* PointerReturn()");
        proxy.Should().Contain("public unsafe void FunctionPointerParameter(delegate*<void> callback)");
        proxy.Should().Contain("public unsafe delegate*<int> FunctionPointerReturn()");

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }

    private static CSharpCompilation WithUnsafe(CSharpCompilation compilation) =>
        compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
}
