using System.Buffers;
using MessagePack;
using MessagePack.Resolvers;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Guards that the protocol envelope DTOs round-trip through the production MessagePack
/// serializer (the composite StandardResolver + ContractlessStandardResolver). These types
/// carry no MessagePack attributes, so this is the canary that the contractless resolver
/// still handles them after their declaration shape changes.
/// </summary>
public class EnvelopeRoundTripTests
{
    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return serializer.Deserialize<T>(writer.WrittenMemory);
    }

    [Fact]
    public void RpcRequest_RoundTrips_WithInstanceId()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = "Calc",
            MethodName = "Add",
            InstanceId = "instance-7",
        };

        var result = RoundTrip(serializer, request);

        Assert.Equal(request.MessageId, result.MessageId);
        Assert.Equal(request.ServiceName, result.ServiceName);
        Assert.Equal(request.MethodName, result.MethodName);
        Assert.Equal(request.InstanceId, result.InstanceId);
    }

    [Fact]
    public void RpcRequest_RoundTrips_WithNullInstanceId()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest { MessageId = 1, ServiceName = "S", MethodName = "M" };

        var result = RoundTrip(serializer, request);

        Assert.Equal(1, result.MessageId);
        Assert.Null(result.InstanceId);
    }

    [Fact]
    public void RpcRequest_RoundTrips_WithStreams()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = "Streaming",
            MethodName = "Upload",
            Streams = new[]
            {
                new RpcStreamHandle(101, RpcStreamKind.Binary),
                new RpcStreamHandle(102, RpcStreamKind.Items),
            },
        };

        var result = RoundTrip(serializer, request);

        Assert.NotNull(result.Streams);
        Assert.Collection(
            result.Streams!,
            stream =>
            {
                Assert.Equal(101, stream.StreamId);
                Assert.Equal(RpcStreamKind.Binary, stream.Kind);
            },
            stream =>
            {
                Assert.Equal(102, stream.StreamId);
                Assert.Equal(RpcStreamKind.Items, stream.Kind);
            });
    }

    [Fact]
    public void RpcRequest_ReusesRegisteredServiceAndMethodNames()
    {
        var serializer = new MessagePackRpcSerializer();
        var serviceName = new string("CachedService".ToCharArray());
        var methodName = new string("CachedMethod".ToCharArray());
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = serviceName,
            MethodName = methodName,
        };

        var result = RoundTrip(serializer, request);

        Assert.Same(serviceName, result.ServiceName);
        Assert.Same(methodName, result.MethodName);
    }

    [Fact]
    public void RpcRequest_RemainsReadableByContractlessResolver()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = "CompatService",
            MethodName = "CompatMethod",
            InstanceId = "instance-7",
        };
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, request);

        var result = MessagePackSerializer.Deserialize<RpcRequest>(
            writer.WrittenMemory,
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance));

        Assert.Equal(request.MessageId, result.MessageId);
        Assert.Equal(request.ServiceName, result.ServiceName);
        Assert.Equal(request.MethodName, result.MethodName);
        Assert.Equal(request.InstanceId, result.InstanceId);
    }

    [Fact]
    public void RpcRequest_ReadsContractlessResolverBytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest
        {
            MessageId = 43,
            ServiceName = "CompatService",
            MethodName = "CompatMethod",
            InstanceId = "instance-8",
        };
        var bytes = MessagePackSerializer.Serialize(
            request,
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance));

        var result = serializer.Deserialize<RpcRequest>(bytes);

        Assert.Equal(request.MessageId, result.MessageId);
        Assert.Equal(request.ServiceName, result.ServiceName);
        Assert.Equal(request.MethodName, result.MethodName);
        Assert.Equal(request.InstanceId, result.InstanceId);
    }

    [Theory]
    [InlineData(false, false, true, false, "ServiceName")]
    [InlineData(true, true, true, false, "ServiceName")]
    [InlineData(true, false, false, false, "MethodName")]
    [InlineData(true, false, true, true, "MethodName")]
    public void RpcRequest_MissingRequiredNames_Throws(
        bool includeServiceName,
        bool nilServiceName,
        bool includeMethodName,
        bool nilMethodName,
        string missingName)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var messagePackWriter = new MessagePackWriter(writer);
        var fieldCount = 1 + (includeServiceName ? 1 : 0) + (includeMethodName ? 1 : 0);
        messagePackWriter.WriteMapHeader(fieldCount);
        messagePackWriter.Write("MessageId");
        messagePackWriter.Write(42);

        if (includeServiceName)
        {
            messagePackWriter.Write("ServiceName");
            if (nilServiceName)
            {
                messagePackWriter.WriteNil();
            }
            else
            {
                messagePackWriter.Write("Svc");
            }
        }

        if (includeMethodName)
        {
            messagePackWriter.Write("MethodName");
            if (nilMethodName)
            {
                messagePackWriter.WriteNil();
            }
            else
            {
                messagePackWriter.Write("Op");
            }
        }

        messagePackWriter.Flush();

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(writer.WrittenMemory));
        Assert.Contains(missingName, ex.ToString());
    }

    [Fact]
    public void RpcResponse_RoundTrips_Success()
    {
        var serializer = new MessagePackRpcSerializer();
        var response = new RpcResponse { MessageId = 9, IsSuccess = true };

        var result = RoundTrip(serializer, response);

        Assert.Equal(9, result.MessageId);
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorType);
    }

    [Fact]
    public void RpcResponse_RoundTrips_Error()
    {
        var serializer = new MessagePackRpcSerializer();
        var response = new RpcResponse
        {
            MessageId = 9,
            IsSuccess = false,
            ErrorMessage = "boom",
            ErrorType = "ShaRpcRemoteException",
        };

        var result = RoundTrip(serializer, response);

        Assert.Equal(9, result.MessageId);
        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.ErrorMessage);
        Assert.Equal("ShaRpcRemoteException", result.ErrorType);
    }

    [Fact]
    public void RpcResponse_RoundTrips_WithStream()
    {
        var serializer = new MessagePackRpcSerializer();
        var response = new RpcResponse
        {
            MessageId = 9,
            IsSuccess = true,
            Stream = new RpcStreamHandle(201, RpcStreamKind.Binary),
        };

        var result = RoundTrip(serializer, response);

        Assert.NotNull(result.Stream);
        Assert.Equal(201, result.Stream!.Value.StreamId);
        Assert.Equal(RpcStreamKind.Binary, result.Stream.Value.Kind);
    }

    [Fact]
    public void ServiceHandle_RoundTrips()
    {
        var serializer = new MessagePackRpcSerializer();
        var handle = new ServiceHandle { ServiceName = "ISub", InstanceId = "sub-1" };

        var result = RoundTrip(serializer, handle);

        Assert.Equal("ISub", result.ServiceName);
        Assert.Equal("sub-1", result.InstanceId);
    }

    [Fact]
    public void NullableServiceHandle_RoundTrips_Null()
    {
        // The dispatcher emits serializer.Serialize<ServiceHandle?>(output, null) when a nullable
        // sub-service method returns null; the client deserializes the same nullable shape.
        var serializer = new MessagePackRpcSerializer();

        var result = RoundTrip<ServiceHandle?>(serializer, null);

        Assert.Null(result);
    }

    [Fact]
    public void NullableServiceHandle_RoundTrips_Value()
    {
        var serializer = new MessagePackRpcSerializer();
        ServiceHandle? handle = new ServiceHandle { ServiceName = "ISub", InstanceId = "sub-2" };

        var result = RoundTrip(serializer, handle);

        Assert.NotNull(result);
        Assert.Equal("sub-2", result!.Value.InstanceId);
    }
}
