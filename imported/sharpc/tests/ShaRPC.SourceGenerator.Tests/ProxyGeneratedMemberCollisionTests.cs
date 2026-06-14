using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class ProxyGeneratedMemberCollisionTests
{
    [Fact]
    public void ServiceMethodsNamedLikeGeneratedProxyFields_UseExplicitInterfaceImplementations()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ProxyGeneratedMembers
            {
                [ShaRpcService]
                public interface IFieldCollision
                {
                    int _invoker();
                    Task<int> _instanceId();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFieldCollision.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("int global::Regress.ProxyGeneratedMembers.IFieldCollision._invoker()");
        proxy.Should().Contain(
            "global::System.Threading.Tasks.Task<int> global::Regress.ProxyGeneratedMembers.IFieldCollision._instanceId()");
        proxy.Should().Contain(
            "public global::System.Threading.Tasks.Task<int> _invokerAsync(");
        proxy.Should().Contain(
            "global::System.Threading.Tasks.Task<int> global::Regress.ProxyGeneratedMembers.IFieldCollisionAsync._instanceId(");
        proxy.Should().NotContain("public int _invoker()");
        proxy.Should().NotContain("public async global::System.Threading.Tasks.Task<int> _instanceId(");
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
