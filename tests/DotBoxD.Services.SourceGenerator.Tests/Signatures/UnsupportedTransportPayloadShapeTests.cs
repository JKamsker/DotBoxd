using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class UnsupportedTransportPayloadShapeTests
{
    [Fact]
    public void RpcStreamHandleParameter_ProducesDBXS002_AndSkipsDispatch()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using DotBoxD.Services.Protocol;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedTransportPayloads
            {
                [DotBoxDService]
                public interface IRawStreamHandle
                {
                    Task<int> UploadAsync(RpcStreamHandle stream);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("parameter 'stream'") &&
            d.GetMessage().Contains("RpcStreamHandle"));

        var generated = runResult.Results.Single().GeneratedSources;
        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IRawStreamHandle.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"UploadAsync\":");
    }

    [Fact]
    public void NestedStreamingAndControlPayloads_ProduceDBXS002_AndCompilingProxyStubs()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedTransportPayloads
            {
                [DotBoxDService]
                public interface ITransportPayloads
                {
                    Task<int> UploadManyAsync(Stream[] streams);
                    Task<int> UploadListAsync(List<Stream> streams);
                    Task<int> UploadAsyncEnumerableAsync(Dictionary<string, IAsyncEnumerable<int>> streams);
                    Task<int> UploadPipeTupleAsync((Pipe Pipe, int Id) request);
                    Task<int> TokenPayloadAsync(CancellationToken? token);
                    Task<int> TokenArrayAsync(CancellationToken[] tokens);
                    Task<Stream[]> DownloadManyAsync();
                    ValueTask<Pipe[]> DownloadPipesAsync();
                    Task<CancellationToken> ReturnTokenAsync();
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == "DBXS002" &&
                d.GetMessage().Contains("streaming or control type as an RPC payload"))
            .ToArray();
        diagnostics.Should().HaveCount(9);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("ITransportPayloads.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("UploadManyAsync(global::System.IO.Stream[] streams)");
        proxy.Should().Contain("TokenPayloadAsync(global::System.Threading.CancellationToken? token)");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("ITransportPayloads.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"UploadManyAsync\":");
        dispatcher.Should().NotContain("case \"DownloadManyAsync\":");
        dispatcher.Should().NotContain("case \"ReturnTokenAsync\":");
    }

    [Fact]
    public void DirectStreamingAndCancellationControlShapes_RemainSupported()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedTransportPayloads
            {
                [DotBoxDService]
                public interface IDirectTransportShapes
                {
                    Task<int> UploadStreamAsync(Stream stream, CancellationToken ct = default);
                    Task<int> UploadPipeAsync(Pipe pipe);
                    Task<int> UploadItemsAsync(IAsyncEnumerable<int> items);
                    Task<Stream> DownloadStreamAsync();
                    ValueTask<Pipe> DownloadPipeAsync();
                    Task<IAsyncEnumerable<int>> DownloadItemsAsync();
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");
    }

    private static GeneratorDriverRunResult Compile(string source)
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

        return runResult;
    }
}
