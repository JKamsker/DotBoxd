using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class NullableSubServiceTests
{
    [Fact]
    public void NullableSubServiceReturn_UsesNonNullableIdentityForProxyType()
    {
        const string source = """
            #nullable enable
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableSubService
            {
                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
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
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableRejectedSubService
            {
                public interface ISubAsync
                {
                }

                [ShaRpcService]
                public interface ISub
                {
                    int Count();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated async sibling interface 'ISubAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("global::Regress.NullableRejectedSubService.ISub"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
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
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableValueTaskSubService
            {
                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    ValueTask<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
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
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NullableRejectedValueTaskSubService
            {
                public interface ISubAsync
                {
                }

                [ShaRpcService]
                public interface ISub
                {
                    int Count();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    ValueTask<ISub?> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated async sibling interface 'ISubAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("global::Regress.NullableRejectedValueTaskSubService.ISub"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("Sub?Proxy");
        proxy.Should().NotContain("new global::Regress.NullableRejectedValueTaskSubService.SubProxy");
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
