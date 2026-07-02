using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class SubServiceAvailabilityTests
{
    [Fact]
    public void RootMethodReturningRejectedSubService_BecomesUnsupportedStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceAvailability
            {
                [RpcService]
                public interface ISub
                {
                    int Count { get; }
                }

                [RpcService]
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

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("interface property 'Count' is not supported"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Regress.SubServiceAvailability.SubProxy");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
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

    [Fact]
    public void RootMethodReturningSubServiceRejectedByGeneratedTypeCollision_BecomesUnsupportedStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceAvailability
            {
                public sealed class SubProxy
                {
                }

                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService]
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

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated proxy type 'SubProxy' would collide"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Regress.SubServiceAvailability.SubProxy");

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }

    [Fact]
    public void RootPropertyReturningRejectedSubService_RejectsRootService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.SubServiceAvailability
            {
                [RpcService]
                public interface ISub
                {
                    int Count { get; }
                }

                [RpcService]
                public interface IRoot
                {
                    ISub Sub { get; }
                }
            }
            """;

        var runResult = Run(source, out var finalCompilation);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("interface property 'Count' is not supported"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("property 'Sub' cannot be proxied because that service was not generated"));
        runResult.Results.Single().GeneratedSources.Should().NotContain(
            g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"));
        runResult.Results.Single().GeneratedSources.Should().NotContain(
            g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"));

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }

    [Fact]
    public void RootMethodReturningSubServiceRejectedByWireNameCollision_BecomesUnsupportedStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceAvailability
            {
                [RpcService(Name = "dup")]
                public interface ISubA
                {
                    Task<int> AAsync();
                }

                [RpcService(Name = "dup")]
                public interface ISubB
                {
                    Task<int> BAsync();
                }

                [RpcService(Name = "root")]
                public interface IRoot
                {
                    Task<ISubA> GetSubAsync();
                }
            }
            """;

        var runResult = Run(source, out var finalCompilation);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("wire service name 'dup'"));
        AssertRootSubServiceStub(runResult, finalCompilation, "SubAProxy");
    }

    [Fact]
    public void RootMethodReturningSubServiceRejectedByGeneratedServiceNameCollision_BecomesUnsupportedStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceAvailability
            {
                [RpcService(Name = "ifoo")]
                public interface IFoo
                {
                    Task<int> AAsync();
                }

                [RpcService(Name = "foo")]
                public interface Foo
                {
                    Task<int> BAsync();
                }

                [RpcService(Name = "root")]
                public interface IRoot
                {
                    Task<IFoo> GetSubAsync();
                }
            }
            """;

        var runResult = Run(source, out var finalCompilation);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("generated proxy and dispatcher type names"));
        AssertRootSubServiceStub(runResult, finalCompilation, "FooProxy");
    }

    private static GeneratorDriverRunResult Run(string source, out CSharpCompilation finalCompilation)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        return runResult;
    }

    private static void AssertRootSubServiceStub(
        GeneratorDriverRunResult runResult,
        CSharpCompilation finalCompilation,
        string rejectedProxyName)
    {
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Regress.SubServiceAvailability." + rejectedProxyName);

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
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
