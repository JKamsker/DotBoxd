using System.IO.Pipelines;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public Task<Stream> InvokeStreamAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, instanceId: null, ct));

    public Task<Stream> InvokeStreamAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, request, instanceId: null, streams, ct));

    public Task<Pipe> InvokePipeAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, instanceId: null, ct));

    public Task<Pipe> InvokePipeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, request, instanceId: null, streams, ct));

    public IAsyncEnumerable<T> InvokeAsyncEnumerable<T>(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, instanceId: null, invokeCt),
            ct);

    public IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, request, instanceId: null, streams, invokeCt),
            ct);

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<T>(
        string service,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(SendRequestAsync(service, method, instanceId: null, ct));

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(
            SendRequestAsync(service, method, request, instanceId: null, streams, ct));
}
