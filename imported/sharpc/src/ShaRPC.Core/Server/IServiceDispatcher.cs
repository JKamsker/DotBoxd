using System.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core.Server;

/// <summary>
/// Interface for generated service dispatchers that route incoming requests to the
/// appropriate service method. Root-service dispatchers use <see cref="DispatchAsync"/>;
/// sub-service dispatchers also implement <see cref="DispatchOnInstanceAsync"/> to route
/// calls to a particular registered instance.
/// </summary>
public interface IServiceDispatcher
{
    /// <summary>The RPC service name this dispatcher handles.</summary>
    string ServiceName { get; }

    /// <summary>
    /// Dispatches a singleton-service request to the appropriate method and serializes the result
    /// into <paramref name="output"/>. Writing straight into the caller's buffer lets the server
    /// append the result behind the response envelope without an intermediate buffer and copy; a
    /// void/Task-returning method writes nothing. <paramref name="registry"/> is the per-connection
    /// instance registry — dispatchers ignore it unless the method returns a sub-service interface,
    /// in which case they register the returned instance and serialize a
    /// <see cref="ShaRPC.Core.Protocol.ServiceHandle"/>.
    /// </summary>
    Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default);

    /// <summary>
    /// Dispatches a singleton-service request with access to streamed arguments and streamed
    /// responses. Existing dispatchers fall back to the non-streaming overload.
    /// </summary>
    Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        IRpcStreamingContext streaming,
        CancellationToken ct = default) =>
        DispatchAsync(method, payload, serializer, registry, output, ct);

    /// <summary>
    /// Dispatches a call to a specific server-side instance previously registered with
    /// <see cref="IInstanceRegistry.Register"/>, serializing the result into <paramref name="output"/>.
    /// Default implementation throws — the generator only emits an override for service dispatchers
    /// that may be reached as sub-services.
    /// </summary>
    Task DispatchOnInstanceAsync(
        string instanceId,
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) =>
        throw new ShaRpcNotFoundException(
            $"Service '{ServiceName}' does not support instance-scoped dispatch.",
            ShaRpcNotFoundException.NotFoundKind.Instance);

    /// <summary>
    /// Dispatches an instance-scoped request with access to streamed arguments and streamed
    /// responses. Existing dispatchers fall back to the non-streaming overload.
    /// </summary>
    Task DispatchOnInstanceAsync(
        string instanceId,
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        IRpcStreamingContext streaming,
        CancellationToken ct = default) =>
        DispatchOnInstanceAsync(instanceId, method, payload, serializer, registry, output, ct);
}
