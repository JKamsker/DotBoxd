namespace DotBoxD.Services.SourceGenerator.Infrastructure;

/// <summary>
/// Names of the runtime members that generated proxies and dispatchers bind to by string. These are
/// extracted from the emitters so that <c>ServicesGeneratorMemberNameContractTests</c> can pin each one
/// to the real symbol via <c>nameof</c>, turning red the moment a bound member is renamed, removed, or
/// moved in <c>DotBoxD.Services</c>. Each nested class mirrors the owning runtime type.
/// </summary>
internal static class ServicesGeneratorMemberNames
{
    /// <summary>Members of <c>DotBoxD.Services.Server.IRpcInvoker</c> the client proxy calls.</summary>
    public static class RpcInvoker
    {
        public const string InvokeAsync = "InvokeAsync";
        public const string InvokeOnInstanceAsync = "InvokeOnInstanceAsync";
        public const string InvokeValueAsync = "InvokeValueAsync";
        public const string InvokeValueOnInstanceAsync = "InvokeValueOnInstanceAsync";
        public const string InvokeStreamAsync = "InvokeStreamAsync";
        public const string InvokeStreamOnInstanceAsync = "InvokeStreamOnInstanceAsync";
        public const string InvokePipeAsync = "InvokePipeAsync";
        public const string InvokePipeOnInstanceAsync = "InvokePipeOnInstanceAsync";
        public const string InvokeAsyncEnumerable = "InvokeAsyncEnumerable";
        public const string InvokeAsyncEnumerableOnInstance = "InvokeAsyncEnumerableOnInstance";
        public const string InvokeAsyncEnumerableAsync = "InvokeAsyncEnumerableAsync";
        public const string InvokeAsyncEnumerableOnInstanceAsync = "InvokeAsyncEnumerableOnInstanceAsync";
        public const string ReserveStream = "ReserveStream";
        public const string ReleaseStream = "ReleaseStream";
    }

    /// <summary>Members of <c>DotBoxD.Services.Server.IServiceDispatcher</c> the dispatcher implements.</summary>
    public static class ServiceDispatcher
    {
        public const string ServiceName = "ServiceName";
        public const string DispatchAsync = "DispatchAsync";
        public const string DispatchOnInstanceAsync = "DispatchOnInstanceAsync";
    }

    /// <summary>Members of <c>DotBoxD.Services.Server.IInstanceRegistry</c> the dispatcher calls.</summary>
    public static class InstanceRegistry
    {
        public const string TryGet = "TryGet";
        public const string Register = "Register";
        public const string ReleaseAsync = "ReleaseAsync";
    }

    /// <summary>Members of <c>DotBoxD.Services.Serialization.ISerializer</c> the generated code calls.</summary>
    public static class Serializer
    {
        public const string Serialize = "Serialize";
        public const string Deserialize = "Deserialize";
    }

    /// <summary>Members of <c>DotBoxD.Services.Streaming.Remote.IRpcStreamingContext</c> plus the
    /// <c>RpcStreamingContext.Disabled</c> sentinel the dispatcher references.</summary>
    public static class RpcStreamingContext
    {
        public const string GetStream = "GetStream";
        public const string GetPipe = "GetPipe";
        public const string GetAsyncEnumerable = "GetAsyncEnumerable";
        public const string SetResponse = "SetResponse";
        public const string Disabled = "Disabled";
    }

    /// <summary>Factory members of <c>DotBoxD.Services.Streaming.Frames.RpcStreamAttachment</c>.</summary>
    public static class RpcStreamAttachment
    {
        public const string FromStream = "FromStream";
        public const string FromPipe = "FromPipe";
        public const string FromAsyncEnumerable = "FromAsyncEnumerable";
    }

    /// <summary>Members of the <c>DotBoxD.Services.Protocol.RpcStreamKind</c> enum.</summary>
    public static class RpcStreamKind
    {
        public const string Binary = "Binary";
        public const string Items = "Items";
    }

    /// <summary>Members of <c>DotBoxD.Services.Peer.RpcPeer</c> the generated extension methods call.</summary>
    public static class RpcPeer
    {
        public const string Provide = "Provide";
        public const string Get = "Get";
    }

    /// <summary>Members of <c>DotBoxD.Services.Generated.GeneratedServiceRegistry</c> the factory calls.</summary>
    public static class GeneratedServiceRegistry
    {
        public const string RegisterServices = "RegisterServices";
        public const string Register = "Register";
        public const string CreateProxy = "CreateProxy";
        public const string CreateDispatcher = "CreateDispatcher";
    }

    /// <summary>The registration member shared by <c>IRpcServiceRegistrationSink</c> and
    /// <c>IRpcGeneratedServiceRegistrationSink</c> in <c>DotBoxD.Services.Generated</c>.</summary>
    public static class ServiceRegistrationSink
    {
        public const string AddService = "AddService";
    }

    /// <summary>Members of the <c>DotBoxD.Services.Protocol.ServiceHandle</c> struct.</summary>
    public static class ServiceHandle
    {
        public const string ServiceName = "ServiceName";
        public const string InstanceId = "InstanceId";
    }

    /// <summary>Members of <c>DotBoxD.Services.Exceptions.ServiceNotFoundException.NotFoundKind</c>.</summary>
    public static class NotFoundKind
    {
        public const string Instance = "Instance";
        public const string Service = "Service";
        public const string Method = "Method";
    }

    /// <summary>Members of the <c>DotBoxD.Services.Generated.GeneratedReturnKind</c> enum. The emitter's
    /// internal <c>MethodReturnKind</c> is translated to these names, so a rename on the runtime enum must
    /// turn this contract red.</summary>
    public static class GeneratedReturnKind
    {
        public const string Void = "Void";
        public const string Sync = "Sync";
        public const string SyncNestedService = "SyncNestedService";
        public const string Task = "Task";
        public const string TaskOfT = "TaskOfT";
        public const string ValueTask = "ValueTask";
        public const string ValueTaskOfT = "ValueTaskOfT";
        public const string TaskOfNestedService = "TaskOfNestedService";
        public const string ValueTaskOfNestedService = "ValueTaskOfNestedService";
        public const string AsyncEnumerable = "AsyncEnumerable";
        public const string TaskOfAsyncEnumerable = "TaskOfAsyncEnumerable";
        public const string ValueTaskOfAsyncEnumerable = "ValueTaskOfAsyncEnumerable";
        public const string Stream = "Stream";
        public const string TaskOfStream = "TaskOfStream";
        public const string ValueTaskOfStream = "ValueTaskOfStream";
        public const string Pipe = "Pipe";
        public const string TaskOfPipe = "TaskOfPipe";
        public const string ValueTaskOfPipe = "ValueTaskOfPipe";
    }
}
