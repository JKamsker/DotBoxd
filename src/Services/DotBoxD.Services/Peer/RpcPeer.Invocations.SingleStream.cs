using System.IO.Pipelines;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Peer;

public sealed partial class RpcPeer
{
    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeAsync<TRequest, TResponse>(service, method, request, stream, ct);

    public Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeAsync(service, method, request, stream, ct);

    public Task<Stream> InvokeStreamAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeStreamAsync(service, method, request, stream, ct);

    public Task<Pipe> InvokePipeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokePipeAsync(service, method, request, stream, ct);

    public IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerable<TRequest, T>(service, method, request, stream, ct);

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableAsync<TRequest, T>(service, method, request, stream, ct);

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, stream, ct);

    public Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync(service, instanceId, method, request, stream, ct);

    public Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeStreamOnInstanceAsync(service, instanceId, method, request, stream, ct);

    public Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokePipeOnInstanceAsync(service, instanceId, method, request, stream, ct);

    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableOnInstance<TRequest, T>(service, instanceId, method, request, stream, ct);

    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment stream,
        CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(service, instanceId, method, request, stream, ct);
}
