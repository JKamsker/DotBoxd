using FluentAssertions;
using Microsoft.CodeAnalysis;
namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public void NestedServiceInterface_ProducesDBXS003_AndIsSkipped()
    {
        // Regression for H4: nested interfaces are unsupported; DBXS003 is fired and
        // no broken output is emitted.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Nested
            {
                public class Outer
                {
                    [RpcService]
                    public interface IInner
                    {
                        Task<int> DoAsync(int x);
                    }
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003");
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IInner"));
    }

    [Fact]
    public void ExistingAsyncSiblingType_ProducesDBXS003_AndServiceIsSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.GeneratedTypeCollision
            {
                public interface IFooAsync
                {
                }

                [RpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'IFooAsync' would collide"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void NonPublicServiceInterface_ProducesDBXS003_AndIsSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.InternalService
            {
                [RpcService]
                internal interface IInternal
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("service interfaces must be public"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IInternal"));
    }

    [Fact]
    public void ServiceInterfaceWithProperty_ProducesDBXS003_AndIsSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.PropertyMember
            {
                [RpcService]
                public interface IWithProperty
                {
                    int Count { get; }
                    Task<int> CountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("interface property 'Count' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithProperty"));
    }

    [Fact]
    public void ServiceInterfaceWithEvent_ProducesDBXS003_AndIsSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.EventMember
            {
                [RpcService]
                public interface IWithEvent
                {
                    event EventHandler Changed;
                    Task<int> CountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("interface event 'Changed' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithEvent"));
    }

    [Fact]
    public void ServiceInterfaceWithStaticAbstractMethod_ProducesDBXS003_AndIsSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.StaticMember
            {
                [RpcService]
                public interface IWithStatic
                {
                    static abstract Task<int> CountAsync();
                    Task<int> InstanceCountAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("static interface method 'CountAsync' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithStatic"));
    }

    [Fact]
    public void ServiceInterfaceWithPrivateMethod_ProducesDBXS003_AndIsSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.PrivateMethod
            {
                [RpcService]
                public interface IWithPrivate
                {
                    private int Hidden() => 1;
                    int Visible();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("non-public interface method 'Hidden' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IWithPrivate"));
    }

    [Fact]
    public void ReservedKeywordParameterNames_AreEscaped()
    {
        // Regression for H1: a parameter named `default` (or any C# keyword) must be
        // emitted with an @ prefix so the proxy compiles.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Keyword
            {
                [RpcService]
                public interface IKw
                {
                    Task<int> DoAsync(int @class, int @default);
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    [Fact]
    public void EscapedKeywordServiceInterfaceName_CompilesGeneratedOutput()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.KeywordType
            {
                [RpcService]
                public interface @event
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.KeywordType", "event", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("global::Regress.KeywordType.@event");

        var extensions = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "DotBoxDRpcExtensions.g.cs")
            .SourceText.ToString();
        extensions.Should().Contain("global::Regress.KeywordType.@event");
    }

}
