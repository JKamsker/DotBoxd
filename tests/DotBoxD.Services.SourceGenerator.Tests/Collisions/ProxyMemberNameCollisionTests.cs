using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Collisions;

public class ProxyMemberNameCollisionTests
{
    [Fact]
    public void ServiceMethodNamedLikeProxyClass_UsesExplicitInterfaceImplementation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.ProxyMemberName
            {
                [RpcService]
                public interface IFoo
                {
                    int FooProxy();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("int global::Regress.ProxyMemberName.IFoo.FooProxy()");
        proxy.Should().NotContain("public int FooProxy()");
    }

    [Fact]
    public void AsyncSiblingMethodNamedLikeProxyClass_UsesExplicitInterfaceImplementation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.ProxyAsyncMemberName
            {
                [RpcService]
                public interface IFoo
                {
                    Task<int> FooProxy(CancellationToken ct = default);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("global::Regress.ProxyAsyncMemberName.IFoo.FooProxy");
        proxy.Should().Contain("global::Regress.ProxyAsyncMemberName.IFooAsync.FooProxy");
        proxy.Should().NotContain("public async global::System.Threading.Tasks.Task<int> FooProxy(");
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
