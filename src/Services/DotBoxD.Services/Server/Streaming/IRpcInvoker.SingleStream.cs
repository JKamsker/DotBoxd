using System.IO.Pipelines;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Server;

public partial interface IRpcInvoker
{
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeAsync<TRequest, TResponse>(service, method, request, [stream], ct);

    Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeAsync(service, method, request, [stream], ct);

    Task<Stream> InvokeStreamAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeStreamAsync(service, method, request, [stream], ct);

    Task<Pipe> InvokePipeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokePipeAsync(service, method, request, [stream], ct);

    IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeAsyncEnumerable<TRequest, T>(service, method, request, [stream], ct);

    Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeAsyncEnumerableAsync<TRequest, T>(service, method, request, [stream], ct);

    Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, [stream], ct);

    Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeOnInstanceAsync(service, instanceId, method, request, [stream], ct);

    Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeStreamOnInstanceAsync(service, instanceId, method, request, [stream], ct);

    Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokePipeOnInstanceAsync(service, instanceId, method, request, [stream], ct);

    IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeAsyncEnumerableOnInstance<TRequest, T>(service, instanceId, method, request, [stream], ct);

    Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(service, instanceId, method, request, [stream], ct);
}
