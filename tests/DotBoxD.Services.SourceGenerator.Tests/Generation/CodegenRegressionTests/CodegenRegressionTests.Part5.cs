using FluentAssertions;
using Microsoft.CodeAnalysis;
namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public void DistinctMethods_WithSameCustomWireName_AreDiagnosedAndOmittedFromDispatcher()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.CustomWireCollision
            {
                [RpcService]
                public interface ILookup
                {
                    [RpcMethod(Name = "lookup")]
                    Task<int> GetByIdAsync(int id);

                    [RpcMethod(Name = "lookup")]
                    Task<string> GetByNameAsync(string name);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002")
            .Should().HaveCount(2, "both methods share the same explicit wire name");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CustomWireCollision", "ILookup", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"lookup\":");
    }

    /// <summary>
    /// Generic service methods are not routable by DotBoxD's current method-name based
    /// protocol. The proxy still has to implement the user interface, so it emits a
    /// generic throwing stub with matching constraints.
    /// </summary>
    [Fact]
    public void GenericServiceMethod_ProducesDBXS002_AndCompilingProxyStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericMethod
            {
                [RpcService]
                public interface IGenericMethod
                {
                    Task<T> EchoAsync<T>(T value) where T : class;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("generic service methods are not supported"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericMethod", "IGenericMethod", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("EchoAsync<T>(T value) where T : class");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericMethod", "IGenericMethod", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"EchoAsync\":");
    }

    [Fact]
    public void GenericServiceMethod_WithKeywordTypeParameter_EmitsCompilingProxyStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericKeyword
            {
                [RpcService]
                public interface IGenericKeyword
                {
                    Task<@class> EchoAsync<@class>(@class value) where @class : class;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("generic service methods are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericKeyword", "IGenericKeyword", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("EchoAsync<@class>(@class value) where @class : class");
        proxy.Should().Contain("global::System.Threading.Tasks.Task<@class>");
    }

    [Fact]
    public void GenericServiceMethod_WithRefStructAntiConstraint_PreservesConstraintOnProxyStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.RefStructAntiConstraint
            {
                [RpcService]
                public interface IRefStructAntiConstraint
                {
                    T Echo<T>(T value) where T : allows ref struct;
                }
            }
            """;

        var (final, runResult) = RunWithPreviewByRefLikeGenerics(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("generic service methods are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.RefStructAntiConstraint",
                "IRefStructAntiConstraint",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("Echo<T>(T value) where T : allows ref struct");
        proxy.Should().Contain("throw new global::System.NotSupportedException");
    }

    /// <summary>
    /// Regression: <see cref="System.Threading.CancellationToken"/> parameters can be
    /// written through aliases and can appear before later payload parameters. The
    /// proxy must preserve the user's signature while excluding the token from the
    /// serialized request tuple, and the dispatcher must pass the runtime token back in
    /// the original argument position.
    /// </summary>
    [Fact]
    public void CancellationTokenAliasInMiddle_PreservesSignatureAndIsNotSerialized()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            using CT = System.Threading.CancellationToken;

            namespace Regress.CtOrder
            {
                [RpcService]
                public interface ICtOrder
                {
                    Task<int> SumAsync(int a, CT cancellationToken, int b);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CtOrder", "ICtOrder", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain(
            "SumAsync(int a, global::System.Threading.CancellationToken cancellationToken, int b)");
        proxy.Should().Contain(
            "InvokeAsync<(int, int), int>(\"ICtOrder\", \"SumAsync\", (a, b), cancellationToken)");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CtOrder", "ICtOrder", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("receiver.SumAsync(args.Item1, ct, args.Item2)");
    }

    // Behavioral test: a ValueTask<T>-returning proxy method must await correctly.

}
