using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class SubServiceFinalRejectionTests
{
    [Fact]
    public void SubServiceRejectedByAsyncSiblingCollision_DoesNotStaleRejectRestoredParentService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.FinalRejectedSubService
            {
                public interface ISubAsync
                {
                }

                public interface IRootAsync
                {
                }

                [RpcService]
                public interface ISub
                {
                    int Count();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<ISub> OpenAsync();
                }

                [RpcService]
                public interface IParent
                {
                    Task<IRoot> GetRootAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'ISubAsync'"));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'IRootAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("IRoot.OpenAsync"));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("IParent.GetRootAsync"));

        var generated = runResult.Results.Single().GeneratedSources;
        var rootProxy = generated
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("throw new global::System.NotSupportedException");
        rootProxy.Should().NotContain("new global::Regress.FinalRejectedSubService.SubProxy");

        var rootDispatcher = generated
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        rootDispatcher.Should().NotContain("case \"OpenAsync\":");

        var parentProxy = generated
            .Single(g => g.HintName.EndsWith("IParent.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        parentProxy.Should().Contain("new global::Regress.FinalRejectedSubService.RootProxy");
        parentProxy.Should().NotContain("DotBoxD cannot marshal 'GetRootAsync'");
    }

    [Fact]
    public void CyclicAsyncSiblingRejections_StubParentInsteadOfReferencingMissingProxy()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.CyclicFinalRejectedSubService
            {
                public interface IAAsync
                {
                }

                public interface IBAsync
                {
                }

                [RpcService]
                public interface IA
                {
                    Task<IB> GetBAsync();
                }

                [RpcService]
                public interface IB
                {
                    Task<IA> GetAAsync();
                }

                [RpcService]
                public interface IParent
                {
                    Task<IA> GetAAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("IParent.GetAAsync"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("IA.GetBAsync"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("IB.GetAAsync"));

        var parentProxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IParent.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        parentProxy.Should().Contain("throw new global::System.NotSupportedException");
        parentProxy.Should().NotContain("new global::Regress.CyclicFinalRejectedSubService.AProxy");
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
