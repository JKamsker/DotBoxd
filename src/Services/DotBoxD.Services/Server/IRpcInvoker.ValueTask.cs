namespace DotBoxD.Services.Server;

public partial interface IRpcInvoker
{
    ValueTask<TResponse> InvokeValueAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        new(InvokeAsync<TRequest, TResponse>(service, method, request, ct));

    ValueTask<TResponse> InvokeValueAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) =>
        new(InvokeAsync<TResponse>(service, method, ct));

    ValueTask InvokeValueAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        new(InvokeAsync(service, method, request, ct));

    ValueTask InvokeValueAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        new(InvokeAsync(service, method, ct));

    ValueTask<TResponse> InvokeValueOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        new(InvokeOnInstanceAsync<TRequest, TResponse>(service, instanceId, method, request, ct));

    ValueTask<TResponse> InvokeValueOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        new(InvokeOnInstanceAsync<TResponse>(service, instanceId, method, ct));

    ValueTask InvokeValueOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        new(InvokeOnInstanceAsync(service, instanceId, method, request, ct));

    ValueTask InvokeValueOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        new(InvokeOnInstanceAsync(service, instanceId, method, ct));
}
