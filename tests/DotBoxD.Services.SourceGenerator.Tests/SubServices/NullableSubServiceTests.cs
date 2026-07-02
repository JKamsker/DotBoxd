using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class NullableSubServiceTests
{
    [Fact]
    public void NullableSubServiceReturn_UsesNonNullableIdentityForProxyType()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableSubService
            {
                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(
            "global::System.Threading.Tasks.Task<global::Regress.NullableSubService.ISub?> OpenAsync()");
        proxy.Should().Contain("new global::Regress.NullableSubService.SubProxy");
        proxy.Should().NotContain("Sub?Proxy");
    }

    [Fact]
    public void NullableRejectedSubServiceReturn_BecomesUnsupportedStub()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableRejectedSubService
            {
                public interface ISubAsync
                {
                }

                [RpcService]
                public interface ISub
                {
                    int Count();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'ISubAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::Regress.NullableRejectedSubService.ISub"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("Sub?Proxy");
        proxy.Should().NotContain("new global::Regress.NullableRejectedSubService.SubProxy");
    }

    [Fact]
    public void NullableValueTaskSubServiceReturn_UsesNonNullableIdentityForProxyType()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableValueTaskSubService
            {
                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    ValueTask<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(
            "global::System.Threading.Tasks.ValueTask<global::Regress.NullableValueTaskSubService.ISub?> OpenAsync()");
        proxy.Should().Contain("new global::Regress.NullableValueTaskSubService.SubProxy");
        proxy.Should().NotContain("Sub?Proxy");
    }

    [Fact]
    public void NullableRejectedValueTaskSubServiceReturn_BecomesUnsupportedStub()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableRejectedValueTaskSubService
            {
                public interface ISubAsync
                {
                }

                [RpcService]
                public interface ISub
                {
                    int Count();
                }

                [RpcService]
                public interface IRoot
                {
                    ValueTask<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'ISubAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::Regress.NullableRejectedValueTaskSubService.ISub"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("Sub?Proxy");
        proxy.Should().NotContain("new global::Regress.NullableRejectedValueTaskSubService.SubProxy");
    }

    [Fact]
    public void NullableSubServiceProperty_RejectsService()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableSubServiceProperty
            {
                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    ISub? Maybe { get; }
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("nullable sub-service property 'Maybe' is not supported"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRoot."));
    }

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    private static void AssertCompiles(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
