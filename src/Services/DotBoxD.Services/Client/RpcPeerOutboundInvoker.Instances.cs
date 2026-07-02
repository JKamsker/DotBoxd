using System.IO.Pipelines;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<TResponse>();
        }

        return SendUnaryRequestAsync<TRequest, TResponse>(service, method, request, instanceId, ct);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        using var received = await SendRequestAsync(service, method, request, instanceId, streams, ct)
            .ConfigureAwait(false);
        return DeserializeNonStreamingResponse<TResponse>(received);
    }

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<TResponse>();
        }

        return SendUnaryRequestAsync<TResponse>(service, method, instanceId, ct);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        using var received = await SendRequestAsync(
            service,
            method,
            request,
            instanceId,
            streams: null,
            ct).ConfigureAwait(false);
        EnsureNoResponsePayload(received);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        using var received = await SendRequestAsync(service, method, request, instanceId, streams, ct)
            .ConfigureAwait(false);
        EnsureNoResponsePayload(received);
    }

    public async Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        using var received = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
        EnsureNoResponsePayload(received);
    }

    public Task<Stream> InvokeStreamOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<Stream>();
        }

        return _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, instanceId, ct));
    }

    public Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<Stream>();
        }

        return _streamingCalls.ReadStreamAsync(SendRequestAsync(service, method, request, instanceId, streams, ct));
    }

    public Task<Pipe> InvokePipeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<Pipe>();
        }

        return _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, instanceId, ct));
    }

    public Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<Pipe>();
        }

        return _streamingCalls.ReadPipeAsync(SendRequestAsync(service, method, request, instanceId, streams, ct));
    }

    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<T>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        return _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, instanceId, invokeCt),
            ct);
    }

    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default)
    {
        instanceId = ValidateInstanceId(instanceId);
        return _streamingCalls.EnumerateAsync<T>(
            invokeCt => SendRequestAsync(service, method, request, instanceId, streams, invokeCt),
            ct);
    }

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<T>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<IAsyncEnumerable<T>>();
        }

        return _streamingCalls.ReadAsyncEnumerableAsync<T>(SendRequestAsync(service, method, instanceId, ct));
    }

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdTask<IAsyncEnumerable<T>>();
        }

        return _streamingCalls.ReadAsyncEnumerableAsync<T>(
            SendRequestAsync(service, method, request, instanceId, streams, ct));
    }
}
