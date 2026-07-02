using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public partial class UnsupportedShapeCoverageTests
{
    [Fact]
    public void RefLikeReturnType_ProducesDBXS002_AndCompilingProxyStub()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface IRefLikeReturn
                {
                    ReadOnlySpan<byte> GetBytes();
                }
            }
            """;

        var (_, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("return type uses a ref-like type"));
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRefLikeReturn.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("global::System.ReadOnlySpan<byte> GetBytes()");
        proxy.Should().Contain("throw new global::System.NotSupportedException");
    }

    [Fact]
    public void MultipleCancellationTokenParameters_ProducesDBXS002_AtSecondToken()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface IMultipleTokens
                {
                    Task<int> BadAsync(int x, CancellationToken first = default, CancellationToken second = default);
                    Task<int> GoodAsync(int x, CancellationToken ct = default);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS002");
        diagnostic.GetMessage().Should().Contain("multiple CancellationToken parameters are not supported");
        DiagnosticText(source, diagnostic).Should().StartWith("second");

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IMultipleTokens.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IMultipleTokens.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"BadAsync\":");
        dispatcher.Should().Contain("case \"GoodAsync\":");
    }

    [Fact]
    public void NestedTaskAndValueTaskPayloads_ProduceDBXS002_AndCompilingProxyStubs()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface INestedAsyncPayloads
                {
                    Task<Task<int>> NestedReturnAsync();
                    ValueTask<ValueTask<int>> NestedValueReturnAsync();
                    Task TakesNestedAsync(Task<int> value);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == "DBXS002" &&
                d.GetMessage().Contains("Task and ValueTask are only supported as top-level return wrappers"))
            .ToArray();
        diagnostics.Should().HaveCount(3);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("INestedAsyncPayloads.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("NestedReturnAsync()");
        proxy.Should().Contain("NestedValueReturnAsync()");
        proxy.Should().Contain("TakesNestedAsync(global::System.Threading.Tasks.Task<int> value)");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("INestedAsyncPayloads.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"NestedReturnAsync\":");
        dispatcher.Should().NotContain("case \"NestedValueReturnAsync\":");
        dispatcher.Should().NotContain("case \"TakesNestedAsync\":");
    }

    [Fact]
    public void ObjectAndDynamicPayloads_ProduceDBXS002_AndSkipDispatch()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface IOpenEndedPayloads
                {
                    object GetObject();
                    Task<dynamic> GetDynamicAsync();
                    void TakeObject(object value);
                    void TakeNestedObject(Dictionary<string, object> values);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == "DBXS002" &&
                d.GetMessage().Contains("object or dynamic as an RPC payload type"))
            .ToArray();
        diagnostics.Should().HaveCount(4);
        diagnostics.Should().Contain(d => d.GetMessage().Contains("return type"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("parameter 'value'"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("parameter 'values'"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IOpenEndedPayloads.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public object GetObject()");
        proxy.Should().Contain("public global::System.Threading.Tasks.Task<dynamic> GetDynamicAsync()");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IOpenEndedPayloads.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetObject\":");
        dispatcher.Should().NotContain("case \"GetDynamicAsync\":");
        dispatcher.Should().NotContain("case \"TakeObject\":");
        dispatcher.Should().NotContain("case \"TakeNestedObject\":");
    }

    [Fact]
    public void InParameter_ProducesDBXS002_AndPreservesProxyStubSignature()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface IInParameter
                {
                    void Inspect(in int value);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("parameter 'value' uses an unsupported pass-by-reference kind 'in'"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IInParameter.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public void Inspect(in int value)");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IInParameter.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"Inspect\":");
    }

    [Fact]
    public void RefReadonlyParameter_ProducesDBXS002_AndPreservesProxyStubSignature()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface IRefReadonlyParameter
                {
                    void Inspect(ref readonly int value);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("parameter 'value' uses an unsupported pass-by-reference kind 'ref readonly'"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IRefReadonlyParameter.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public void Inspect(ref readonly int value)");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IRefReadonlyParameter.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"Inspect\":");
    }

    [Fact]
    public void SubServiceIndexer_ProducesDBXS003_AndSkipsBrokenRootProxy()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.UnsupportedCoverage
            {
                [RpcService]
                public interface IChildService
                {
                    void Ping();
                }

                [RpcService]
                public interface IRootService
                {
                    IChildService this[int id] { get; }
                }
            }
            """;

        var (_, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("indexer") &&
            d.GetMessage().Contains("not supported"));
        runResult.Results.Single().GeneratedSources.Should().NotContain(
            g => g.HintName.EndsWith("IRootService.DotBoxDRpcProxy.g.cs"));
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

    private static string DiagnosticText(string source, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var line = source.Replace("\r\n", "\n").Split('\n')[span.StartLinePosition.Line];
        return line.Substring(span.StartLinePosition.Character);
    }
}
