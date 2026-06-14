using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class GeneratedTypeCollisionTests
{
    [Fact]
    public void ExistingProxyType_ProducesDBXS003_AtCollidingType()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public sealed class FooProxy
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS003");
        diagnostic.GetMessage().Should().Contain("generated proxy type 'FooProxy' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("FooProxy");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void FileLocalGeneratedTypeNameMatch_DoesNotProduceCollision()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                file sealed class FooProxy
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("FooProxy"));
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IFoo.DotBoxdRpcProxy.g.cs"));
    }

    [Fact]
    public void GenericExistingProxyType_DoesNotProduceCollision()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GeneratedTypeCollision
            {
                public sealed class FooProxy<T>
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    Task<int> GetAsync();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("FooProxy"));
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IFoo.DotBoxdRpcProxy.g.cs"));
    }

    [Fact]
    public void NamespaceWithTrivia_StillProducesExistingTypeCollision()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress . GeneratedTypeCollision
            {
                public sealed class FooProxy
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    Task<int> GetAsync();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS003");
        diagnostic.GetMessage().Should().Contain("generated proxy type 'FooProxy' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("FooProxy");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void ExistingDispatcherType_ProducesDBXS003_AndServiceIsSkipped()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public sealed class FooDispatcher
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS003");
        diagnostic.GetMessage().Should().Contain("generated dispatcher type 'FooDispatcher' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("FooDispatcher");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void ExistingAsyncSiblingInterface_ProducesDBXS003_AtCollidingType()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public interface IFooAsync
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS003");
        diagnostic.GetMessage().Should().Contain(
            "generated async sibling interface 'IFooAsync' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("IFooAsync");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void ExistingAsyncSiblingInterface_DoesNotRejectServiceWhenNoSiblingWillBeGenerated()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public interface IFooAsync
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    ref int Bad();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'IFooAsync' would collide"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("return value uses an unsupported pass-by-reference kind"));
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IFoo.DotBoxdRpcProxy.g.cs"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo.DotBoxdRpcAsync.g.cs"));
    }

    [Fact]
    public void ExistingGeneratedExtensionsType_ProducesDBXS003_AndServicesAreSkipped()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace DotBoxd.Services.Generated
            {
                public static class DotBoxdGeneratedExtensions
                {
                }
            }

            namespace Regress.GeneratedTypeCollision
            {
                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = Run(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS003");
        diagnostic.GetMessage().Should().Contain(
            "generated extension type 'DotBoxd.Services.Generated.DotBoxdGeneratedExtensions' would collide");
        DiagnosticText(source, diagnostic).Should().Contain("DotBoxdGeneratedExtensions");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void ServicesWithSameGeneratedTypeNames_ProduceDBXS003_AndAreSkipped()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }

                [DotBoxdService]
                public interface Foo
                {
                    int Baz();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains(
                "generated proxy and dispatcher type names 'FooProxy' and 'FooDispatcher' would collide"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g =>
                g.HintName.Contains("IFoo.") ||
                g.HintName.Contains("Foo."));
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static string DiagnosticText(string source, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var line = source.Replace("\r\n", "\n").Split('\n')[span.StartLinePosition.Line];
        return line.Substring(span.StartLinePosition.Character);
    }
}
