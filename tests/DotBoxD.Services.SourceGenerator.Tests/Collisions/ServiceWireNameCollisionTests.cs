using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Collisions;

public class ServiceWireNameCollisionTests
{
    [Fact]
    public void DuplicateCustomServiceNames_ProduceDBXS003_AndServicesAreSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.WireServiceCollision
            {
                [RpcService(Name = "same")]
                public interface IFoo
                {
                    int Foo();
                }

                [RpcService(Name = "same")]
                public interface IBar
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("wire service name 'same' is used by multiple services"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g =>
                g.HintName.Contains("IFoo.") ||
                g.HintName.Contains("IBar.") ||
                g.HintName == "DotBoxDRpcExtensions.g.cs");
    }

    [Fact]
    public void DuplicateDefaultServiceNamesAcrossNamespaces_ProduceDBXS003()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.WireServiceCollision.A
            {
                [RpcService]
                public interface IFoo
                {
                    int A();
                }
            }

            namespace Regress.WireServiceCollision.B
            {
                [RpcService]
                public interface IFoo
                {
                    int B();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("wire service name 'IFoo' is used by multiple services"));
    }

    private static Microsoft.CodeAnalysis.GeneratorDriverRunResult Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
