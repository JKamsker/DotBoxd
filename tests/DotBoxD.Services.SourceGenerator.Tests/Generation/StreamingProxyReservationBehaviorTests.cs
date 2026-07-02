using System.Reflection;
using System.Runtime.Loader;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class StreamingProxyReservationBehaviorTests
{
    [Fact]
    public async Task GeneratedProxy_ReleasesEarlierStreamReservation_WhenLaterReservationFails()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Behavior.Streaming
            {
                [RpcService]
                public interface IUpload
                {
                    Task<int> UploadAsync(Stream first, Stream second, CancellationToken ct = default);
                }
            }
            """;

        var assembly = CompileWithGenerator(source);
        var proxyType = assembly.GetType("Behavior.Streaming.UploadProxy")!;
        var interfaceType = assembly.GetType("Behavior.Streaming.IUpload")!;
        var invoker = new FailingSecondReservationInvoker();
        var proxy = Activator.CreateInstance(proxyType, invoker)!;
        var upload = interfaceType.GetMethod("UploadAsync")!;

        using var first = new MemoryStream(new byte[] { 1 });
        using var second = new MemoryStream(new byte[] { 2 });
        var task = (Task)upload.Invoke(
            proxy,
            new object[] { first, second, CancellationToken.None })!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        ex.Message.Should().Be("second reservation failed");
        invoker.ReserveKinds.Should().Equal(RpcStreamKind.Binary, RpcStreamKind.Binary);
        invoker.ReleasedStreamIds.Should().Equal(1);
        invoker.InvokeCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GeneratedProxy_ReleasesReservations_WhenStreamedInvokeThrowsSynchronously()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Behavior.Streaming
            {
                [RpcService]
                public interface IUpload
                {
                    Task<int> UploadAsync(Stream first, Stream second, CancellationToken ct = default);
                }
            }
            """;

        var assembly = CompileWithGenerator(source);
        var proxyType = assembly.GetType("Behavior.Streaming.UploadProxy")!;
        var interfaceType = assembly.GetType("Behavior.Streaming.IUpload")!;
        var invoker = new SynchronousRejectingInvoker();
        var proxy = Activator.CreateInstance(proxyType, invoker)!;
        var upload = interfaceType.GetMethod("UploadAsync")!;

        using var first = new MemoryStream(new byte[] { 1 });
        using var second = new MemoryStream(new byte[] { 2 });
        var task = (Task)upload.Invoke(
            proxy,
            new object[] { first, second, CancellationToken.None })!;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        ex.Message.Should().Be("streamed invoke rejected");
        invoker.ReleasedStreamIds.Should().Equal(2, 1);
        invoker.InvokeCalled.Should().BeTrue();
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
        var alc = new AssemblyLoadContext("StreamingProxyReservation_" + Guid.NewGuid(), isCollectible: false);
        return alc.LoadFromStream(ms);
    }

    private sealed class FailingSecondReservationInvoker : IRpcInvoker
    {
        private int _reserveCount;

        public List<RpcStreamKind> ReserveKinds { get; } = new();

        public List<int> ReleasedStreamIds { get; } = new();

        public bool InvokeCalled { get; private set; }

        public RpcStreamHandle ReserveStream(RpcStreamKind kind)
        {
            ReserveKinds.Add(kind);
            var count = Interlocked.Increment(ref _reserveCount);
            if (count == 2)
            {
                throw new InvalidOperationException("second reservation failed");
            }

            return new RpcStreamHandle(count, kind);
        }

        public void ReleaseStream(RpcStreamHandle handle) =>
            ReleasedStreamIds.Add(handle.StreamId);

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[] streams,
            CancellationToken ct = default)
        {
            InvokeCalled = true;
            throw new InvalidOperationException("invoke should not run");
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
    }

    private sealed class SynchronousRejectingInvoker : IRpcInvoker
    {
        private int _reserveCount;

        public List<int> ReleasedStreamIds { get; } = new();

        public bool InvokeCalled { get; private set; }

        public RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
            new(Interlocked.Increment(ref _reserveCount), kind);

        public void ReleaseStream(RpcStreamHandle handle) =>
            ReleasedStreamIds.Add(handle.StreamId);

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(
            string service,
            string method,
            TRequest request,
            RpcStreamAttachment[] streams,
            CancellationToken ct = default)
        {
            InvokeCalled = true;
            throw new InvalidOperationException("streamed invoke rejected");
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
    }
}
