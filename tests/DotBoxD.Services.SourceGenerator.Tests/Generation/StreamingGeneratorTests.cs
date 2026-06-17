using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class StreamingGeneratorTests
{
    [Fact]
    public void StreamingSignatures_GenerateStreamingProxyAndDispatcher()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.Gen
            {
                [DotBoxDService]
                public interface IStreamingService
                {
                    IAsyncEnumerable<int> Numbers(CancellationToken ct = default);
                    Task<Stream> DownloadAsync(int id, CancellationToken ct = default);
                    ValueTask<Pipe> PipeAsync(CancellationToken ct = default);
                    Task<int> UploadAsync(Stream bytes, IAsyncEnumerable<int> items, Pipe pipe, CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        EmitShouldSucceed(finalCompilation);

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Gen",
                "IStreamingService",
                GeneratorTestHelper.GeneratedKind.Proxy));
        var dispatcher = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Gen",
                "IStreamingService",
                GeneratorTestHelper.GeneratedKind.Dispatcher));
        proxy.Should().Contain("InvokeAsyncEnumerable<int>");
        proxy.Should().Contain("InvokeStreamAsync<int>");
        proxy.Should().Contain("InvokePipeAsync");
        proxy.Should().Contain("ReserveStream(global::DotBoxD.Services.Protocol.RpcStreamKind.Binary)");
        proxy.Should().Contain("ReserveStream(global::DotBoxD.Services.Protocol.RpcStreamKind.Items)");
        proxy.Should().Contain("RpcStreamAttachment.FromStream");
        proxy.Should().Contain("RpcStreamAttachment.FromAsyncEnumerable<int>");
        dispatcher.Should().Contain("streaming.GetStream");
        dispatcher.Should().Contain("streaming.GetAsyncEnumerable<int>");
        dispatcher.Should().Contain("streaming.GetPipe");
        dispatcher.Should().Contain("streaming.SetResponse(result)");
    }

    [Fact]
    public void TaskWrappedAsyncEnumerableReturns_UseEagerInvokerApi()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.Tasks
            {
                [DotBoxDService]
                public interface ITaskWrappedStreaming
                {
                    Task<IAsyncEnumerable<int>> NumbersAsync(CancellationToken ct = default);
                    ValueTask<IAsyncEnumerable<string>> NamesAsync(CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        EmitShouldSucceed(((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees));

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Tasks",
                "ITaskWrappedStreaming",
                GeneratorTestHelper.GeneratedKind.Proxy));
        proxy.Should().Contain("InvokeAsyncEnumerableAsync<int>");
        proxy.Should().Contain("InvokeAsyncEnumerableAsync<string>");
        proxy.Should().Contain("return (this._instanceId is null ? this._invoker.InvokeAsyncEnumerableAsync<int>");
        proxy.Should().Contain("return new global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IAsyncEnumerable<string>>((this._instanceId is null ? this._invoker.InvokeAsyncEnumerableAsync<string>");
        proxy.Should().NotContain("return (this._instanceId is null ? this._invoker.InvokeAsyncEnumerable<int>");
        proxy.Should().NotContain("return (this._instanceId is null ? this._invoker.InvokeAsyncEnumerable<string>");
    }

    [Fact]
    public void DirectAsyncEnumerableWithStreamedArguments_ReservesStreamsInsideEnumeration()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading;

            namespace Streaming.Lazy
            {
                [DotBoxDService]
                public interface ILazyStreaming
                {
                    IAsyncEnumerable<int> Echo(Stream bytes, IAsyncEnumerable<int> items, CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        EmitShouldSucceed(((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees));

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Lazy",
                "ILazyStreaming",
                GeneratorTestHelper.GeneratedKind.Proxy));
        var returnIndex = proxy.IndexOf("return __dotboxd_enumerate(ct);", StringComparison.Ordinal);
        var reserveIndex = proxy.IndexOf("ReserveStream(global::DotBoxD.Services.Protocol.RpcStreamKind.Binary)", StringComparison.Ordinal);
        returnIndex.Should().BeGreaterOrEqualTo(0);
        reserveIndex.Should().BeGreaterThan(returnIndex);
        proxy.Should().Contain("async global::System.Collections.Generic.IAsyncEnumerable<int> __dotboxd_enumerate");
        proxy.Should().Contain("[global::System.Runtime.CompilerServices.EnumeratorCancellation]");
        proxy.Should().Contain("__dotboxd_streams, __dotboxd_enumerationCt)");
        proxy.Should().Contain(".GetAsyncEnumerator(__dotboxd_enumerationCt)");
        proxy.Should().NotContain("TaskAsyncEnumerableExtensions");
        proxy.Should().Contain("this._invoker.ReleaseStream(__dotboxd_stream1)");
        proxy.Should().Contain("this._invoker.ReleaseStream(__dotboxd_stream2)");
    }

    [Fact]
    public void StreamedArgumentReservations_AreInsideReservationCleanupTry()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.ReservationCleanup
            {
                [DotBoxDService]
                public interface IUpload
                {
                    Task<int> UploadAsync(Stream first, Stream second, CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        EmitShouldSucceed(((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees));

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.ReservationCleanup",
                "IUpload",
                GeneratorTestHelper.GeneratedKind.Proxy));
        var methodIndex = proxy.IndexOf("UploadAsync", StringComparison.Ordinal);
        var firstDeclaration = proxy.IndexOf("RpcStreamHandle __dotboxd_stream1 = default;", methodIndex, StringComparison.Ordinal);
        var tryIndex = proxy.IndexOf("try", firstDeclaration, StringComparison.Ordinal);
        var firstReserve = proxy.IndexOf("__dotboxd_stream1 = this._invoker.ReserveStream", tryIndex, StringComparison.Ordinal);
        var secondReserve = proxy.IndexOf("__dotboxd_stream2 = this._invoker.ReserveStream", firstReserve, StringComparison.Ordinal);
        var firstRelease = proxy.IndexOf("this._invoker.ReleaseStream(__dotboxd_stream1)", secondReserve, StringComparison.Ordinal);
        var secondRelease = proxy.IndexOf("this._invoker.ReleaseStream(__dotboxd_stream2)", secondReserve, StringComparison.Ordinal);

        firstDeclaration.Should().BeGreaterOrEqualTo(0);
        tryIndex.Should().BeGreaterThan(firstDeclaration);
        firstReserve.Should().BeGreaterThan(tryIndex);
        secondReserve.Should().BeGreaterThan(firstReserve);
        firstRelease.Should().BeGreaterThan(secondReserve);
        secondRelease.Should().BeGreaterThan(secondReserve);
        proxy.Should().Contain("if (__dotboxd_stream1Reserved)");
        proxy.Should().Contain("if (__dotboxd_stream2Reserved)");
    }

    [Fact]
    public void AsyncSiblingDirectAsyncEnumerableWithAddedCt_ReservesStreamsInsideEnumeration()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;

            namespace Streaming.LazySibling
            {
                [DotBoxDService]
                public interface ILazySiblingStreaming
                {
                    IAsyncEnumerable<int> EchoAsync(Stream bytes);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        EmitShouldSucceed(((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees));

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.LazySibling",
                "ILazySiblingStreaming",
                GeneratorTestHelper.GeneratedKind.Proxy));
        var signature = "global::System.Collections.Generic.IAsyncEnumerable<int> EchoAsync(global::System.IO.Stream bytes, global::System.Threading.CancellationToken ct = default)";
        var signatureIndex = proxy.IndexOf(signature, StringComparison.Ordinal);
        var returnIndex = proxy.IndexOf("return __dotboxd_enumerate(ct);", signatureIndex, StringComparison.Ordinal);
        var reserveIndex = proxy.IndexOf("ReserveStream(global::DotBoxD.Services.Protocol.RpcStreamKind.Binary)", signatureIndex, StringComparison.Ordinal);

        signatureIndex.Should().BeGreaterOrEqualTo(0);
        returnIndex.Should().BeGreaterThan(signatureIndex);
        reserveIndex.Should().BeGreaterThan(returnIndex);
        proxy.Substring(signatureIndex).Should().Contain("[global::System.Runtime.CompilerServices.EnumeratorCancellation]");
    }

    [Fact]
    public void NullableStreamingShapes_ProduceUnsupportedDiagnostics()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.IO.Pipelines;
            using System.Threading.Tasks;

            namespace Streaming.Nullable
            {
                [DotBoxDService]
                public interface INullableStreaming
                {
                    Stream? Download();
                    Task<Stream?> DownloadAsync();
                    IAsyncEnumerable<int>? Numbers();
                    Task<IAsyncEnumerable<int>?> NumbersAsync();
                    IAsyncEnumerable<string?> NullableItems();
                    Task<IAsyncEnumerable<string?>> NullableItemsAsync();
                    Task<int> UploadStreamAsync(Stream? bytes);
                    Task<int> UploadPipeAsync(Pipe? pipe);
                    Task<int> UploadItemsAsync(IAsyncEnumerable<int>? items);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(d => d.Id == "DBXS002")
            .Select(d => d.GetMessage())
            .ToArray();

        diagnostics.Should().HaveCount(7);
        diagnostics.Should().Contain(m => m.Contains("Download") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("DownloadAsync") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("Numbers") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("NumbersAsync") &&
            m.Contains("nullable streaming return values are not supported"));
        diagnostics.Should().Contain(m => m.Contains("nullable streamed parameter 'bytes'"));
        diagnostics.Should().Contain(m => m.Contains("nullable streamed parameter 'pipe'"));
        diagnostics.Should().Contain(m => m.Contains("nullable streamed parameter 'items'"));
    }

    [Fact]
    public void StreamingGeneratedLocals_AreReservedAcrossParameters()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Streaming.Locals
            {
                [DotBoxDService]
                public interface ILocalCollisionStreaming
                {
                    Task<int> UploadAsync(
                        Stream s1,
                        Stream s2,
                        Stream s3,
                        Stream s4,
                        Stream s5,
                        Stream s6,
                        Stream s7,
                        Stream s8,
                        Stream s9,
                        Stream s10,
                        Stream s11,
                        int __dotboxd_stream1,
                        int __dotboxd_arg1,
                        CancellationToken ct = default);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        EmitShouldSucceed(((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees));

        var proxy = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Locals",
                "ILocalCollisionStreaming",
                GeneratorTestHelper.GeneratedKind.Proxy));
        var dispatcher = GeneratedSource(
            runResult,
            GeneratorTestHelper.HintName(
                "Streaming.Locals",
                "ILocalCollisionStreaming",
                GeneratorTestHelper.GeneratedKind.Dispatcher));

        proxy.Should().Contain("RpcStreamHandle __dotboxd_stream11 = default;");
        proxy.Should().Contain("RpcStreamHandle __dotboxd_stream111 = default;");
        dispatcher.Should().Contain("var __dotboxd_arg11 =");
        dispatcher.Should().Contain("var __dotboxd_arg111 =");
    }

    private static string GeneratedSource(
        GeneratorDriverRunResult runResult,
        string hintName) =>
        runResult.Results.Single().GeneratedSources
            .Single(source => source.HintName == hintName)
            .SourceText
            .ToString();

    private static void EmitShouldSucceed(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("generated streaming code must compile");
    }
}
