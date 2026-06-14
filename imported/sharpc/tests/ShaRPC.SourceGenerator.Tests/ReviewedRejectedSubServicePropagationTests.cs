using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class ReviewedRejectedSubServicePropagationTests
{
    [Fact]
    public void CrossNamespaceTaskAndValueTaskRejectedSubServices_BecomeUnsupportedStubs()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Reviewed.RejectedSubService.Sub
            {
                [ShaRpcService(Name = "dup")]
                public interface ISubA
                {
                    Task<int> AAsync();
                }

                [ShaRpcService(Name = "dup")]
                public interface ISubB
                {
                    Task<int> BAsync();
                }
            }

            namespace Reviewed.RejectedSubService.Root
            {
                [ShaRpcService(Name = "root")]
                public interface IRoot
                {
                    Task<Reviewed.RejectedSubService.Sub.ISubA> OpenTaskAsync();
                    ValueTask<Reviewed.RejectedSubService.Sub.ISubA> OpenValueTaskAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("wire service name 'dup'"));
        runResult.Diagnostics.Where(d => d.Id == "SHARPC002")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains(
                "global::Reviewed.RejectedSubService.Sub.ISubA"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public global::System.Threading.Tasks.Task<global::Reviewed.RejectedSubService.Sub.ISubA> OpenTaskAsync()");
        proxy.Should().Contain("public global::System.Threading.Tasks.ValueTask<global::Reviewed.RejectedSubService.Sub.ISubA> OpenValueTaskAsync()");
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Reviewed.RejectedSubService.Sub.SubAProxy");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"OpenTaskAsync\":");
        dispatcher.Should().NotContain("case \"OpenValueTaskAsync\":");
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
