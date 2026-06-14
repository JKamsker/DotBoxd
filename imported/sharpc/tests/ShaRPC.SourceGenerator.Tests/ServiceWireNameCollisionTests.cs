using System.Linq;
using FluentAssertions;

namespace ShaRPC.SourceGenerator.Tests;

public class ServiceWireNameCollisionTests
{
    [Fact]
    public void DuplicateCustomServiceNames_ProduceSHARPC003_AndServicesAreSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.WireServiceCollision
            {
                [ShaRpcService(Name = "same")]
                public interface IFoo
                {
                    int Foo();
                }

                [ShaRpcService(Name = "same")]
                public interface IBar
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("wire service name 'same' is used by multiple services"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g =>
                g.HintName.Contains("IFoo.") ||
                g.HintName.Contains("IBar.") ||
                g.HintName == "ShaRpcExtensions.g.cs");
    }

    [Fact]
    public void DuplicateDefaultServiceNamesAcrossNamespaces_ProduceSHARPC003()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.WireServiceCollision.A
            {
                [ShaRpcService]
                public interface IFoo
                {
                    int A();
                }
            }

            namespace Regress.WireServiceCollision.B
            {
                [ShaRpcService]
                public interface IFoo
                {
                    int B();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "SHARPC003")
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
