using System;
using System.Threading;
using System.Threading.Tasks;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Test-only adapters over the dispatcher's <see cref="System.Buffers.IBufferWriter{T}"/> API.
/// They run the real <c>DispatchAsync</c>/<c>DispatchOnInstanceAsync</c> against a pooled writer
/// and hand the written bytes back as an owned <see cref="Payload"/> (no copy), so existing tests
/// can keep asserting on a response payload without each call site managing its own writer.
/// </summary>
internal static class DispatcherTestExtensions
{
    public static async Task<Payload> DispatchToPayloadAsync(
        this IServiceDispatcher dispatcher,
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        CancellationToken ct = default)
    {
        var writer = new PooledBufferWriter();
        try
        {
            await dispatcher.DispatchAsync(method, payload, serializer, registry, writer, ct);
            return writer.DetachPayload();
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    public static async Task<Payload> DispatchOnInstanceToPayloadAsync(
        this IServiceDispatcher dispatcher,
        string instanceId,
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        CancellationToken ct = default)
    {
        var writer = new PooledBufferWriter();
        try
        {
            await dispatcher.DispatchOnInstanceAsync(instanceId, method, payload, serializer, registry, writer, ct);
            return writer.DetachPayload();
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }
}
