using System.IO.Pipelines;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, stream, ct)
            .ConfigureAwait(false);
        return DeserializeNonStreamingResponse<TResponse>(received);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, stream, ct)
            .ConfigureAwait(false);
        EnsureNonStreamingResponse(received);
    }

    public Task<Stream> InvokeStreamAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, request, instanceId: null, stream, ct));

    public Task<Pipe> InvokePipeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, request, instanceId: null, stream, ct));

    public IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, request, instanceId: null, stream, invokeCt),
            ct);

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _streamingCalls.ReadAsyncEnumerableAsync<T>(
            SendRequestAsync(service, method, request, instanceId: null, stream, ct));

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        using var received = await SendRequestAsync(service, method, request, instanceId, stream, ct)
            .ConfigureAwait(false);
        return DeserializeNonStreamingResponse<TResponse>(received);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        using var received = await SendRequestAsync(service, method, request, instanceId, stream, ct)
            .ConfigureAwait(false);
        EnsureNonStreamingResponse(received);
    }

    public Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<Stream>();
        }

        return _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, request, instanceId, stream, ct));
    }

    public Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<Pipe>();
        }

        return _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, request, instanceId, stream, ct));
    }

    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        return _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, request, instanceId, stream, invokeCt),
            ct);
    }

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<IAsyncEnumerable<T>>();
        }

        return _streamingCalls.ReadAsyncEnumerableAsync<T>(
            SendRequestAsync(service, method, request, instanceId, stream, ct));
    }
}
