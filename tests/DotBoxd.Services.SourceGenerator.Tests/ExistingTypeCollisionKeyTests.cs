using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class ExistingTypeCollisionKeyTests
{
    [Fact]
    public void GenericGeneratedTypeNames_DoNotCollideWithNonGenericGeneratedOutputs()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.ExistingTypeKeys
            {
                public sealed class FooDispatcher<T>
                {
                }

                public interface IFooAsync<T>
                {
                }

                public sealed class Outer
                {
                    public sealed class FooProxy
                    {
                    }
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }
            }

            namespace DotBoxd.Services.Generated
            {
                public static class DotBoxdGeneratedExtensions<T>
                {
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Regress_ExistingTypeKeys_IFoo.DotBoxdRpcProxy.g.cs");
        hints.Should().Contain("Regress_ExistingTypeKeys_IFoo.DotBoxdRpcDispatcher.g.cs");
        hints.Should().Contain("Regress_ExistingTypeKeys_IFoo.DotBoxdRpcAsync.g.cs");
        hints.Should().Contain("DotBoxdRpcExtensions.g.cs");
    }

    [Fact]
    public void TopLevelDelegateAndEnumGeneratedTypeNameMatches_ProduceCollisions()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.ExistingTypeKeys
            {
                public delegate void FooProxy();

                public enum BarDispatcher
                {
                    Value
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }

                [DotBoxdService]
                public interface IBar
                {
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2);
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated proxy type 'FooProxy' would collide"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated dispatcher type 'BarDispatcher' would collide"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo.") || g.HintName.Contains("IBar."));
    }

    [Fact]
    public void EscapedKeywordNamespace_StillMatchesExistingTypeCollision()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace Regress.@event
            {
                public sealed class FooProxy
                {
                }

                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated proxy type 'FooProxy' would collide"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
