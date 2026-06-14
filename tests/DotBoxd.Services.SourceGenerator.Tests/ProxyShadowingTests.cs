using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class ProxyShadowingTests
{
    [Fact]
    public void ProxyParametersNamedLikeGeneratedFields_DoNotShadowProxyState()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.ProxyShadow
            {
                [DotBoxdService]
                public interface IShadow
                {
                    Task<int> EchoAsync(int _invoker, string _instanceId, CancellationToken ct = default);
                }
            }
            """;

        var (_, runResult) = Compile(source);
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IShadow.DotBoxdRpcProxy.g.cs"))
            .SourceText.ToString();

        proxy.Should().Contain("this._instanceId is null");
        proxy.Should().Contain("this._invoker.InvokeAsync");
        proxy.Should().Contain("this._invoker.InvokeOnInstanceAsync");
    }

    [Fact]
    public void SubServiceProxyHandleLocal_AvoidsUserParameterNames()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ProxyShadow
            {
                [DotBoxdService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [DotBoxdService]
                public interface IRoot
                {
                    Task<ISub> GetAsync(string __dotboxd_handle);
                }
            }
            """;

        var (_, runResult) = Compile(source);
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxdRpcProxy.g.cs"))
            .SourceText.ToString();

        proxy.Should().Contain("var __dotboxd_handle1 = await");
        proxy.Should().Contain("new global::Regress.ProxyShadow.SubProxy(this._invoker, __dotboxd_handle1.InstanceId)");
    }

    private static (Compilation Compilation, GeneratorDriverRunResult RunResult) Compile(string source)
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

        return (finalCompilation, runResult);
    }
}
