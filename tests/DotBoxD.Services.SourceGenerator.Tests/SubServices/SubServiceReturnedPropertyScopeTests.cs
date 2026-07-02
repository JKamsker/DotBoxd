using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class SubServiceReturnedPropertyScopeTests
{
    [Fact]
    public void MethodReturningSubServiceWithSubServiceProperty_ProducesDBXS002()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ReturnedSubServiceScope
            {
                [RpcService]
                public interface IChild
                {
                    Task<int> PingAsync();
                }

                [RpcService]
                public interface ISub
                {
                    IChild Child { get; }
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<ISub> OpenAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("IRoot.OpenAsync") &&
            d.GetMessage().Contains("global::Regress.ReturnedSubServiceScope.ISub") &&
            d.GetMessage().Contains("sub-service property 'Child'") &&
            d.GetMessage().Contains("instance-scoped"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Regress.ReturnedSubServiceScope.SubProxy");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"OpenAsync\":");

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
