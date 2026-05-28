using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class SubServiceAvailabilityTests
{
    [Fact]
    public void RootMethodReturningRejectedSubService_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceAvailability
            {
                [ShaRpcService]
                public interface ISub
                {
                    int Count { get; }
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<ISub> GetSubAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("interface property 'Count' is not supported"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Regress.SubServiceAvailability.SubProxy");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetSubAsync\":");

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
