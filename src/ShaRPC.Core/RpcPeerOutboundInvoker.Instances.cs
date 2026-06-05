using ShaRPC.Core.Streaming;
using System.IO.Pipelines;

namespace ShaRPC.Core;

internal sealed partial class RpcPeerOutboundInvoker
{
    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(
            service,
            method,
            request,
            instanceId,
            streams: null,
            ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, streams, ct)
            .ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(
            service,
            method,
            request,
            instanceId,
            streams: null,
            ct).ConfigureAwait(false);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, request, instanceId, streams, ct)
            .ConfigureAwait(false);
    }

    public async Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
    }

    public Task<Stream> InvokeStreamOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, instanceId, ct));

    public Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, request, instanceId, streams, ct));

    public Task<Pipe> InvokePipeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, instanceId, ct));

    public Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, request, instanceId, streams, ct));

    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<T>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, instanceId, invokeCt),
            ct);

    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, request, instanceId, streams, invokeCt),
            ct);

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<T>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(SendRequestAsync(service, method, instanceId, ct));

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(
            SendRequestAsync(service, method, request, instanceId, streams, ct));
}
