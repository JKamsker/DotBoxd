using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class StreamingProxyEnumeratorCancellationTests
{
    [Fact]
    public async Task Lazy_async_enumerable_setup_uses_enumerator_cancellation_token()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading;

            namespace Behavior.StreamingCancellation
            {
                [DotBoxDService]
                public interface ILazyStreaming
                {
                    IAsyncEnumerable<int> Echo(Stream bytes, IAsyncEnumerable<int> items, CancellationToken ct = default);
                }
            }
            """;

        var assembly = CompileWithGenerator(source);
        var proxyType = assembly.GetType("Behavior.StreamingCancellation.LazyStreamingProxy")!;
        var interfaceType = assembly.GetType("Behavior.StreamingCancellation.ILazyStreaming")!;
        var invoker = new RecordingAsyncEnumerableInvoker();
        var proxy = Activator.CreateInstance(proxyType, invoker)!;
        var echo = interfaceType.GetMethod("Echo")!;

        using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
        var sequence = (IAsyncEnumerable<int>)echo.Invoke(
            proxy,
            [bytes, EmptyItems(), CancellationToken.None])!;
        using var enumeration = new CancellationTokenSource();
        await enumeration.CancelAsync();

        await using var enumerator = sequence.GetAsyncEnumerator(enumeration.Token);
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync().AsTask());

        ex.CancellationToken.Should().Be(enumeration.Token);
        invoker.LastStreamingCancellationToken.Should().Be(enumeration.Token);
    }

    private static Assembly CompileWithGenerator(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException("Emit failed:\n" + errors);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("StreamingProxyEnumeratorCancellation_" + Guid.NewGuid(), isCollectible: false);
        return alc.LoadFromStream(ms);
    }

    private static async IAsyncEnumerable<int> EmptyItems(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.WaitAsync(cancellationToken);
        yield break;
    }

    private sealed class RecordingAsyncEnumerableInvoker : IRpcInvoker
    {
        private int _nextStreamId;

        public CancellationToken LastStreamingCancellationToken { get; private set; }

        public RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
            new(Interlocked.Increment(ref _nextStreamId), kind);

        public void ReleaseStream(RpcStreamHandle handle)
        {
        }

        public IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[]? streams = null,
            CancellationToken ct = default)
        {
            LastStreamingCancellationToken = ct;
            ct.ThrowIfCancellationRequested();
            return Empty<T>();
        }

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeAsync<TResponse>(
            string service,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeAsync<TRequest>(
            string service,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            string service,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeOnInstanceAsync<TRequest>(
            string service,
            string instanceId,
            string method,
            TRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InvokeOnInstanceAsync(
            string service,
            string instanceId,
            string method,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        private static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
