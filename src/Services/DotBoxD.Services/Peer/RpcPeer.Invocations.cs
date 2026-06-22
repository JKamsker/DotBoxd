using System.IO.Pipelines;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;

namespace DotBoxD.Services.Peer;

public sealed partial class RpcPeer
{
    public Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeAsync<TRequest, TResponse>(service, method, request, ct);
    public ValueTask<TResponse> InvokeValueAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeValueAsync<TRequest, TResponse>(service, method, request, ct);
    public Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, RpcStreamAttachment[] streams, CancellationToken ct = default) =>
        _outbound.InvokeAsync<TRequest, TResponse>(service, method, request, streams, ct);
    public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsync<TResponse>(service, method, ct);
    public ValueTask<TResponse> InvokeValueAsync<TResponse>(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeValueAsync<TResponse>(service, method, ct);
    public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeAsync(service, method, request, ct);
    public ValueTask InvokeValueAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeValueAsync(service, method, request, ct);
    public Task InvokeAsync<TRequest>(string service, string method, TRequest request, RpcStreamAttachment[] streams, CancellationToken ct = default) =>
        _outbound.InvokeAsync(service, method, request, streams, ct);
    public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsync(service, method, ct);
    public ValueTask InvokeValueAsync(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeValueAsync(service, method, ct);
    public Task<Stream> InvokeStreamAsync(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeStreamAsync(service, method, ct);
    public Task<Stream> InvokeStreamAsync<TRequest>(string service, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokeStreamAsync(service, method, request, streams, ct);
    public Task<Pipe> InvokePipeAsync(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokePipeAsync(service, method, ct);
    public Task<Pipe> InvokePipeAsync<TRequest>(string service, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokePipeAsync(service, method, request, streams, ct);
    public IAsyncEnumerable<T> InvokeAsyncEnumerable<T>(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerable<T>(service, method, ct);
    public IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(string service, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerable<TRequest, T>(service, method, request, streams, ct);
    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<T>(string service, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableAsync<T>(service, method, ct);
    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(string service, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableAsync<TRequest, T>(service, method, request, streams, ct);
    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, ct);
    public ValueTask<TResponse> InvokeValueOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeValueOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, ct);
    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, RpcStreamAttachment[] streams, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, streams, ct);
    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync<TResponse>(service, instanceId, method, ct);
    public ValueTask<TResponse> InvokeValueOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeValueOnInstanceAsync<TResponse>(service, instanceId, method, ct);
    public Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync(service, instanceId, method, request, ct);
    public ValueTask InvokeValueOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default) =>
        _outbound.InvokeValueOnInstanceAsync(service, instanceId, method, request, ct);
    public Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, RpcStreamAttachment[] streams, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync(service, instanceId, method, request, streams, ct);
    public Task InvokeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeOnInstanceAsync(service, instanceId, method, ct);
    public ValueTask InvokeValueOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeValueOnInstanceAsync(service, instanceId, method, ct);
    public Task<Stream> InvokeStreamOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeStreamOnInstanceAsync(service, instanceId, method, ct);
    public Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokeStreamOnInstanceAsync(service, instanceId, method, request, streams, ct);
    public Task<Pipe> InvokePipeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokePipeOnInstanceAsync(service, instanceId, method, ct);
    public Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokePipeOnInstanceAsync(service, instanceId, method, request, streams, ct);
    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<T>(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableOnInstance<T>(service, instanceId, method, ct);
    public IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(string service, string instanceId, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableOnInstance<TRequest, T>(service, instanceId, method, request, streams, ct);
    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<T>(string service, string instanceId, string method, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableOnInstanceAsync<T>(service, instanceId, method, ct);
    public Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(string service, string instanceId, string method, TRequest request, RpcStreamAttachment[]? streams = null, CancellationToken ct = default) =>
        _outbound.InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(service, instanceId, method, request, streams, ct);
    public RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
        _outbound.ReserveStream(kind);
    public void ReleaseStream(RpcStreamHandle handle) =>
        _outbound.ReleaseStream(handle);
}
