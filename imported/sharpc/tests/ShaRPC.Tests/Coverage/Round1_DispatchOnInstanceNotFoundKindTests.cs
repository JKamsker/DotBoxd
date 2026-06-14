using System.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 1 regression coverage for the <see cref="IServiceDispatcher.DispatchOnInstanceAsync"/>
/// default interface member. A hand-written dispatcher that does not override the instance-scoped
/// entry point must signal a not-found result whose <see cref="ShaRpcNotFoundException.Kind"/> is
/// <see cref="ShaRpcNotFoundException.NotFoundKind.Instance"/> — the service exists, but it does not
/// support instance-scoped dispatch. The default member currently uses the single-arg
/// <c>ShaRpcNotFoundException(string)</c> ctor, which chains to <c>NotFoundKind.Service</c>, so the
/// wire-level error type is wrongly reported as ServiceNotFound instead of InstanceNotFound.
/// </summary>
public sealed class Round1_DispatchOnInstanceNotFoundKindTests
{
    [Fact]
    public async Task DispatchOnInstanceAsync_DefaultMember_ThrowsNotFound_WithInstanceKind()
    {
        // Arrange: a hand-written dispatcher that only handles root-service dispatch and relies on
        // the interface default for instance-scoped calls.
        IServiceDispatcher dispatcher = new RootOnlyDispatcher();
        var output = new ArrayBufferWriter<byte>();

        // Act: invoking the default member must reject the call as not-found.
        var ex = await Assert.ThrowsAsync<ShaRpcNotFoundException>(() =>
            dispatcher.DispatchOnInstanceAsync(
                "instance-1",
                "Method",
                ReadOnlyMemory<byte>.Empty,
                new ThrowingSerializer(),
                new InstanceRegistry(),
                output));

        // Assert: the service exists but does not support instance-scoped dispatch, so the correct
        // classification is Instance (maps to InstanceNotFound on the wire), not Service.
        Assert.Equal(ShaRpcNotFoundException.NotFoundKind.Instance, ex.Kind);
    }

    private sealed class RootOnlyDispatcher : IServiceDispatcher
    {
        public string ServiceName => "RootOnly";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ThrowingSerializer : ISerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, T value) => throw new NotSupportedException();

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
    }
}
