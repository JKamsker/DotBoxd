using System.Buffers;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Behavioral coverage for the public diagnostics hook, error/exception types, attributes, and
/// event-args records. <see cref="RpcDiagnostics.Report"/> is internal, but it is reachable from a
/// real public path: <see cref="InstanceRegistry"/> reports a faulting sub-service dispose through
/// it during teardown. <see cref="RpcDiagnostics.Error"/> is a process-wide static event, so the
/// trigger tests serialize on a shared gate and filter to their own marker operation to stay
/// deterministic even when the rest of the suite runs in parallel.
/// </summary>
public sealed class DiagnosticsErrorsCoverageTests
{
    // RpcDiagnostics.Error is a static event shared by the whole process. Serialize the
    // subscribe/trigger/unsubscribe tests so handlers from one test never observe another's report.
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    [Fact]
    public async Task RpcDiagnostics_Error_RaisedWithOperationAndError_OnFaultingInstanceDispose()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var observed = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var boom = new InvalidOperationException("dispose blew up");

            void Handler(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                // Only react to the fault our own throwing disposable produced.
                if (ReferenceEquals(args.Error, boom))
                {
                    observed.TrySetResult(args);
                }
            }

            RpcDiagnostics.Error += Handler;
            try
            {
                var registry = new InstanceRegistry();
                registry.Register("svc", new ThrowingDisposable(boom));

                // ReleaseAll disposes the registered instance; the dispose throws and the registry
                // must route that fault to diagnostics instead of breaking teardown.
                registry.ReleaseAll();

                var args = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));

                Assert.Same(boom, args.Error);
                Assert.False(string.IsNullOrEmpty(args.Operation));
                // RpcDiagnostics raises Error with a null sender on both the normal and retry paths.
            }
            finally
            {
                RpcDiagnostics.Error -= Handler;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    [Fact]
    public async Task RpcDiagnostics_Error_AfterUnsubscribe_HandlerNoLongerInvoked()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var invocations = 0;
            var boom = new InvalidOperationException("second fault");

            void Handler(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, boom))
                {
                    Interlocked.Increment(ref invocations);
                }
            }

            RpcDiagnostics.Error += Handler;
            RpcDiagnostics.Error -= Handler;

            var registry = new InstanceRegistry();
            registry.Register("svc", new ThrowingDisposable(boom));
            registry.ReleaseAll();

            // Give any erroneous async dispatch a chance, then assert the unsubscribed handler stayed
            // silent. Disposal reporting is synchronous within ReleaseAll, so the count is final here.
            Assert.Equal(0, Volatile.Read(ref invocations));
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    [Fact]
    public async Task RpcDiagnostics_Error_FaultingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var boom = new InvalidOperationException("isolation fault");
            var secondObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Faulting(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, boom))
                {
                    throw new Exception("handler is hostile");
                }
            }

            void Good(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, boom))
                {
                    secondObserved.TrySetResult(true);
                }
            }

            RpcDiagnostics.Error += Faulting;
            RpcDiagnostics.Error += Good;
            try
            {
                var registry = new InstanceRegistry();
                registry.Register("svc", new ThrowingDisposable(boom));

                // A throwing subscriber must not stop the next subscriber from seeing the event, and
                // must not bubble out of teardown.
                registry.ReleaseAll();

                Assert.True(await secondObserved.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            }
            finally
            {
                RpcDiagnostics.Error -= Faulting;
                RpcDiagnostics.Error -= Good;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    [Fact]
    public void RpcDiagnosticErrorEventArgs_ExposesConstructorValues()
    {
        var error = new InvalidOperationException("kaboom");

        var args = new RpcDiagnosticErrorEventArgs("teardown", error);

        Assert.Equal("teardown", args.Operation);
        Assert.Same(error, args.Error);
        Assert.IsAssignableFrom<EventArgs>(args);
    }

    // --- InstanceRegistry observable behavior gaps ---

    [Fact]
    public void InstanceRegistry_TryGet_RegisteredInstance_ReturnsTrueAndInstance()
    {
        var registry = new InstanceRegistry();
        var instance = new object();
        var id = registry.Register("svc", instance);

        var found = registry.TryGet("svc", id, out var resolved);

        Assert.True(found);
        Assert.Same(instance, resolved);
    }

    [Fact]
    public void InstanceRegistry_TryGet_UnknownInstance_ReturnsFalseAndNull()
    {
        var registry = new InstanceRegistry();

        var found = registry.TryGet("svc", "does-not-exist", out var resolved);

        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void InstanceRegistry_TryGet_AfterRelease_ReturnsFalse()
    {
        var registry = new InstanceRegistry();
        var id = registry.Register("svc", new object());

        registry.Release("svc", id);

        Assert.False(registry.TryGet("svc", id, out _));
    }

    [Fact]
    public void InstanceRegistry_Register_NullInstance_ThrowsArgumentNull()
    {
        var registry = new InstanceRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register("svc", null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void InstanceRegistry_Constructor_NonPositiveMax_Throws(int maxInstances)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstanceRegistry(maxInstances));
    }

    // --- IServiceDispatcher interface default member ---

    [Fact]
    public async Task IServiceDispatcher_DispatchOnInstanceAsync_DefaultMember_ThrowsNotFound()
    {
        // A dispatcher that does not override the instance-scoped entry point falls through to the
        // interface default implementation, which must reject the call as not-found.
        IServiceDispatcher dispatcher = new RootOnlyDispatcher();
        var output = new ArrayBufferWriter<byte>();

        var ex = await Assert.ThrowsAsync<ServiceNotFoundException>(() =>
            dispatcher.DispatchOnInstanceAsync(
                "instance-1",
                "Method",
                ReadOnlyMemory<byte>.Empty,
                new ThrowingSerializer(),
                new InstanceRegistry(),
                output));

        Assert.Contains("RootOnly", ex.Message);
        Assert.Contains("instance-scoped dispatch", ex.Message);
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

    private sealed class ThrowingDisposable : IDisposable
    {
        private readonly Exception _error;

        public ThrowingDisposable(Exception error) => _error = error;

        public void Dispose() => throw _error;
    }

    private sealed class ThrowingSerializer : ISerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, T value) => throw new NotSupportedException();

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
    }
}

/// <summary>
/// Direct construction coverage for the public error/exception types and the wire error-type
/// constants. These are simple value carriers; the assertions pin their public contract.
/// </summary>
public sealed class RpcErrorTypesCoverageTests
{
    [Fact]
    public void RpcErrorInfo_FromException_CopiesMessageAndRuntimeTypeName()
    {
        var error = new InvalidOperationException("boom");

        var info = RpcErrorInfo.FromException(error);

        Assert.Equal("boom", info.Message);
        Assert.Equal(nameof(InvalidOperationException), info.Type);
    }

    [Fact]
    public void RpcErrorInfo_Constructor_StoresMessageAndType()
    {
        var info = new RpcErrorInfo("safe message", "AppError");

        Assert.Equal("safe message", info.Message);
        Assert.Equal("AppError", info.Type);
    }

    [Fact]
    public void RpcErrorInfo_ValueEquality_HoldsForSameContent()
    {
        var a = new RpcErrorInfo("m", "t");
        var b = new RpcErrorInfo("m", "t");
        var c = new RpcErrorInfo("m", "other");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void RpcErrorTypes_DefinesDistinctNonEmptyWireNames()
    {
        var names = new[]
        {
            RpcErrorTypes.InternalError,
            RpcErrorTypes.InboundRejected,
            RpcErrorTypes.ServiceNotFound,
            RpcErrorTypes.MethodNotFound,
            RpcErrorTypes.InstanceNotFound,
            RpcErrorTypes.QueueFull,
            RpcErrorTypes.ProtocolError,
        };

        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Equal("RpcInternalError", RpcErrorTypes.InternalError);
        Assert.Equal("DotBoxDServiceNotFound", RpcErrorTypes.ServiceNotFound);
    }
}

/// <summary>
/// Construction and property coverage for the DotBoxD exception hierarchy.
/// </summary>
public sealed class ServiceExceptionCoverageTests
{
    [Fact]
    public void ServiceException_Parameterless_HasNoCustomMessageAndIsException()
    {
        var ex = new ServiceException();

        Assert.IsAssignableFrom<Exception>(ex);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void ServiceException_WithMessage_StoresMessage()
    {
        var ex = new ServiceException("explained");

        Assert.Equal("explained", ex.Message);
    }

    [Fact]
    public void ServiceException_WithInnerException_StoresBoth()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new ServiceException("wrapper", inner);

        Assert.Equal("wrapper", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void RemoteServiceException_ExposesRemoteType()
    {
        var ex = new RemoteServiceException("remote failed", "AppCustomError");

        Assert.Equal("remote failed", ex.Message);
        Assert.Equal("AppCustomError", ex.RemoteExceptionType);
        Assert.IsAssignableFrom<ServiceException>(ex);
    }

    [Fact]
    public void ServiceConnectionException_WithInner_StoresBoth()
    {
        var inner = new IOException("socket gone");
        var ex = new ServiceConnectionException("connection lost", inner);

        Assert.Equal("connection lost", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ServiceTimeoutException_StoresMessage()
    {
        var ex = new ServiceTimeoutException("timed out");

        Assert.Equal("timed out", ex.Message);
        Assert.IsAssignableFrom<ServiceException>(ex);
    }

    [Fact]
    public void ServiceProtocolException_StoresMessage()
    {
        var ex = new ServiceProtocolException("bad frame");

        Assert.Equal("bad frame", ex.Message);
        Assert.IsAssignableFrom<ServiceException>(ex);
    }

    [Fact]
    public void ServiceNotFoundException_DefaultKind_IsService()
    {
        var ex = new ServiceNotFoundException("no service");

        Assert.Equal("no service", ex.Message);
        Assert.Equal(ServiceNotFoundException.NotFoundKind.Service, ex.Kind);
    }

    [Theory]
    [InlineData(ServiceNotFoundException.NotFoundKind.Service)]
    [InlineData(ServiceNotFoundException.NotFoundKind.Method)]
    [InlineData(ServiceNotFoundException.NotFoundKind.Instance)]
    public void ServiceNotFoundException_WithKind_StoresKind(ServiceNotFoundException.NotFoundKind kind)
    {
        var ex = new ServiceNotFoundException("missing", kind);

        Assert.Equal(kind, ex.Kind);
        Assert.Equal("missing", ex.Message);
    }
}

/// <summary>
/// Coverage for the DotBoxD marker attributes and their optional custom-name properties.
/// </summary>
public sealed class AttributeCoverageTests
{
    [Fact]
    public void DotBoxDServiceAttribute_DefaultName_IsNull()
    {
        var attribute = new DotBoxDServiceAttribute();

        Assert.Null(attribute.Name);
    }

    [Fact]
    public void DotBoxDServiceAttribute_WithCustomName_ExposesName()
    {
        var attribute = new DotBoxDServiceAttribute { Name = "Custom.Service" };

        Assert.Equal("Custom.Service", attribute.Name);
    }

    [Fact]
    public void DotBoxDServiceAttribute_AppliedToInterface_IsDiscoverableViaReflection()
    {
        var attribute = typeof(IDecoratedService).GetCustomAttributes(typeof(DotBoxDServiceAttribute), false)
            .Cast<DotBoxDServiceAttribute>()
            .Single();

        Assert.Equal("decorated-wire", attribute.Name);
    }

    [Fact]
    public void DotBoxDMethodAttribute_DefaultName_IsNull()
    {
        var attribute = new DotBoxDMethodAttribute();

        Assert.Null(attribute.Name);
    }

    [Fact]
    public void DotBoxDMethodAttribute_WithCustomName_ExposesName()
    {
        var attribute = new DotBoxDMethodAttribute { Name = "CustomMethod" };

        Assert.Equal("CustomMethod", attribute.Name);
    }

    [Fact]
    public void DotBoxDMethodAttribute_AppliedToMethod_IsDiscoverableViaReflection()
    {
        var method = typeof(IDecoratedService).GetMethod(nameof(IDecoratedService.RenamedAsync))!;
        var attribute = method.GetCustomAttributes(typeof(DotBoxDMethodAttribute), false)
            .Cast<DotBoxDMethodAttribute>()
            .Single();

        Assert.Equal("WireMethod", attribute.Name);
    }

    [DotBoxDService(Name = "decorated-wire")]
    private interface IDecoratedService
    {
        [DotBoxDMethod(Name = "WireMethod")]
        Task RenamedAsync(CancellationToken ct = default);
    }
}

/// <summary>
/// Construction and property coverage for the public peer/host event-args records.
/// </summary>
public sealed class EventArgsCoverageTests
{
    [Fact]
    public void RpcDisconnectedEventArgs_GracefulClose_HasNullError()
    {
        var args = new RpcDisconnectedEventArgs("tcp://1.2.3.4:9000", error: null);

        Assert.Equal("tcp://1.2.3.4:9000", args.RemoteEndpoint);
        Assert.Null(args.Error);
    }

    [Fact]
    public void RpcDisconnectedEventArgs_WithError_ExposesError()
    {
        var error = new IOException("reset");
        var args = new RpcDisconnectedEventArgs("ep", error);

        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcReadErrorEventArgs_ExposesEndpointAndError()
    {
        var error = new InvalidOperationException("read failed");
        var args = new RpcReadErrorEventArgs("ep", error);

        Assert.Equal("ep", args.RemoteEndpoint);
        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcProtocolErrorEventArgs_MinimalConstructor_LeavesErrorNull()
    {
        var args = new RpcProtocolErrorEventArgs("ep", messageId: 7, MessageType.Request, "bad header");

        Assert.Equal("ep", args.RemoteEndpoint);
        Assert.Equal(7, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Equal("bad header", args.Message);
        Assert.Null(args.Error);
    }

    [Fact]
    public void RpcProtocolErrorEventArgs_FullConstructor_ExposesError()
    {
        var error = new ServiceProtocolException("decode failed");
        var args = new RpcProtocolErrorEventArgs("ep", 9, MessageType.Response, "decode failed", error);

        Assert.Equal(9, args.MessageId);
        Assert.Equal(MessageType.Response, args.MessageType);
        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcDispatchErrorEventArgs_ExposesAllRequestCoordinates()
    {
        var error = new InvalidOperationException("dispatch failed");
        var args = new RpcDispatchErrorEventArgs(
            "ep",
            messageId: 42,
            serviceName: "Game",
            methodName: "Move",
            instanceId: "inst-7",
            error);

        Assert.Equal("ep", args.RemoteEndpoint);
        Assert.Equal(42, args.MessageId);
        Assert.Equal("Game", args.ServiceName);
        Assert.Equal("Move", args.MethodName);
        Assert.Equal("inst-7", args.InstanceId);
        Assert.Same(error, args.Error);
    }

    [Fact]
    public void RpcDispatchErrorEventArgs_NullInstanceId_IsAllowed()
    {
        var args = new RpcDispatchErrorEventArgs(
            "ep", 1, "Game", "Status", instanceId: null, new Exception("x"));

        Assert.Null(args.InstanceId);
    }

    [Fact]
    public void RpcHostErrorEventArgs_ExposesError()
    {
        var error = new InvalidOperationException("accept failed");
        var args = new RpcHostErrorEventArgs(error);

        Assert.Same(error, args.Error);
    }
}
