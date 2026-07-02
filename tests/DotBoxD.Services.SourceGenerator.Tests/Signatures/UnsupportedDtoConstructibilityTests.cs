using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class UnsupportedDtoConstructibilityTests
{
    [Fact]
    public void DtoAbstractAndInterfaceMembers_ProduceDBXS002_AndSkipDispatch()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedDtoConstructibility
            {
                public interface IRequestBody
                {
                    int Value { get; }
                }

                public abstract class AbstractBody
                {
                    public int Value { get; init; }
                }

                public sealed class InterfaceEnvelope
                {
                    public IRequestBody Body { get; init; } = null!;
                }

                public sealed class AbstractEnvelope
                {
                    public AbstractBody Body { get; init; } = null!;
                }

                [RpcService]
                public interface IConstructibility
                {
                    Task<int> SendInterfaceAsync(InterfaceEnvelope request);
                    Task<int> SendAbstractAsync(AbstractEnvelope request);
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostics = runResult.Diagnostics.Where(d => d.Id == "DBXS002").ToArray();
        diagnostics.Should().HaveCount(2);
        diagnostics.Should().Contain(d => d.GetMessage().Contains("member 'Body'") &&
            d.GetMessage().Contains("interface") &&
            d.GetMessage().Contains("concrete"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("member 'Body'") &&
            d.GetMessage().Contains("abstract") &&
            d.GetMessage().Contains("concrete"));

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("IConstructibility.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().NotContain("case \"SendInterfaceAsync\":");
        dispatcher.Should().NotContain("case \"SendAbstractAsync\":");
    }

    private static GeneratorDriverRunResult Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return runResult;
    }
}
