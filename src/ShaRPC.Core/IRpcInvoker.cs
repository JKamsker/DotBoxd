using System.IO.Pipelines;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

/// <summary>
/// The call surface a generated proxy uses to invoke methods the other side provides.
/// Implemented by <see cref="ShaRPC.Core.RpcPeer"/>. This is the transport-agnostic invoke
/// contract with no notion of "client" or "connect" — a peer simply forwards calls.
/// </summary>
public interface IRpcInvoker
{
    /// <summary>Reserves a stream id for a streamed argument sent by this peer.</summary>
    RpcStreamHandle ReserveStream(RpcStreamKind kind) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC arguments.");

    /// <summary>Releases a stream id reservation that was never attached to an RPC request.</summary>
    void ReleaseStream(RpcStreamHandle handle) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC arguments.");

    /// <summary>Invokes a method with a request body and a response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="request">The request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    ValueTask<TResponse> InvokeValueAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        new(InvokeAsync<TRequest, TResponse>(service, method, request, ct));

    /// <summary>Invokes a method with a request body that references streamed arguments.</summary>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC arguments.");

    /// <summary>Invokes a method with no request body and a response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default);

    ValueTask<TResponse> InvokeValueAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) =>
        new(InvokeAsync<TResponse>(service, method, ct));

    /// <summary>Invokes a method with a request body and no response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="request">The request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>Invokes a no-response method with a request body that references streamed arguments.</summary>
    Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC arguments.");

    /// <summary>Invokes a method with neither a request nor a response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvokeAsync(
        string service,
        string method,
        CancellationToken ct = default);

    Task<Stream> InvokeStreamAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<Stream> InvokeStreamAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<Pipe> InvokePipeAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<Pipe> InvokePipeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    IAsyncEnumerable<T> InvokeAsyncEnumerable<T>(
        string service,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    IAsyncEnumerable<T> InvokeAsyncEnumerable<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<T>(
        string service,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableAsync<TRequest, T>(
        string service,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    /// <summary>Invokes a method on a specific remote sub-service instance.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="instanceId">The target instance identifier.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="request">The request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default);

    ValueTask<TResponse> InvokeValueOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        new(InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, ct));

    Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC arguments.");

    /// <summary>Invokes an instance-scoped method with no request body and a response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="instanceId">The target instance identifier.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default);

    ValueTask<TResponse> InvokeValueOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        new(InvokeOnInstanceAsync<TResponse>(service, instanceId, method, ct));

    /// <summary>Invokes an instance-scoped method with a request body and no response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="instanceId">The target instance identifier.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="request">The request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default);

    Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[] streams,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC arguments.");

    /// <summary>Invokes an instance-scoped method with neither a request nor a response body.</summary>
    /// <param name="service">The remote service name.</param>
    /// <param name="instanceId">The target instance identifier.</param>
    /// <param name="method">The method to invoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default);

    Task<Stream> InvokeStreamOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<Stream> InvokeStreamOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<Pipe> InvokePipeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<Pipe> InvokePipeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<T>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    IAsyncEnumerable<T> InvokeAsyncEnumerableOnInstance<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<T>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");

    Task<IAsyncEnumerable<T>> InvokeAsyncEnumerableOnInstanceAsync<TRequest, T>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        RpcStreamAttachment[]? streams = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("This IRpcInvoker does not support streaming RPC responses.");
}
