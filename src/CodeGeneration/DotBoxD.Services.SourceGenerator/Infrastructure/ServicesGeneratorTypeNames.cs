namespace DotBoxD.Services.SourceGenerator.Infrastructure;

internal static class ServicesGeneratorTypeNames
{
    public const string GlobalPrefix = "global::";

    public const string RpcServiceAttribute = "DotBoxD.Services.Attributes.RpcServiceAttribute";
    public const string RpcMethodAttribute = "DotBoxD.Services.Attributes.RpcMethodAttribute";
    public const string DotBoxDServiceAttribute = "DotBoxD.Services.Attributes.DotBoxDServiceAttribute";
    public const string DotBoxDMethodAttribute = "DotBoxD.Services.Attributes.DotBoxDMethodAttribute";
    public const string CancellationTokenMetadata = "System.Threading.CancellationToken";

    public const string GeneratedNamespace = "DotBoxD.Services.Generated";
    public const string GeneratedFactoryType = "DotBoxDGenerated";
    public const string GeneratedExtensionsType = "DotBoxDGeneratedExtensions";

    public const string SystemCollectionsGenericNamespace = "System.Collections.Generic";
    public const string SystemIoNamespace = "System.IO";
    public const string SystemIoPipelinesNamespace = "System.IO.Pipelines";
    public const string SystemThreadingTasksNamespace = "System.Threading.Tasks";

    public const string GlobalArgumentNullException = GlobalPrefix + "System.ArgumentNullException";
    public const string GlobalArray = GlobalPrefix + "System.Array";
    public const string GlobalBufferWriter = GlobalPrefix + "System.Buffers.IBufferWriter";
    public const string GlobalCancellationToken = GlobalPrefix + CancellationTokenMetadata;
    public const string GlobalException = GlobalPrefix + "System.Exception";
    public const string GlobalIAsyncDisposable = GlobalPrefix + "System.IAsyncDisposable";
    public const string GlobalIAsyncEnumerable = GlobalPrefix + "System.Collections.Generic.IAsyncEnumerable";
    public const string GlobalIDisposable = GlobalPrefix + "System.IDisposable";
    public const string GlobalNotSupportedException = GlobalPrefix + "System.NotSupportedException";
    public const string GlobalObject = GlobalPrefix + "System.Object";
    public const string GlobalOperationCanceledException = GlobalPrefix + "System.OperationCanceledException";
    public const string GlobalReadOnlyList = GlobalPrefix + "System.Collections.Generic.IReadOnlyList";
    public const string GlobalReadOnlyMemory = GlobalPrefix + "System.ReadOnlyMemory";
    public const string GlobalStream = GlobalPrefix + "System.IO.Stream";
    public const string GlobalTask = GlobalPrefix + "System.Threading.Tasks.Task";
    public const string GlobalType = GlobalPrefix + "System.Type";
    public const string GlobalValueTask = GlobalPrefix + "System.Threading.Tasks.ValueTask";
    public const string GlobalEnumeratorCancellationAttribute =
        GlobalPrefix + "System.Runtime.CompilerServices.EnumeratorCancellation";
    public const string GlobalPipe = GlobalPrefix + "System.IO.Pipelines.Pipe";

    public const string GlobalDotBoxDGenerated = GlobalPrefix + GeneratedNamespace + "." + GeneratedFactoryType;
    public const string GlobalGeneratedMethod = GlobalPrefix + GeneratedNamespace + ".GeneratedMethod";
    public const string GlobalGeneratedParameter = GlobalPrefix + GeneratedNamespace + ".GeneratedParameter";
    public const string GlobalGeneratedReturnKind = GlobalPrefix + GeneratedNamespace + ".GeneratedReturnKind";
    public const string GlobalGeneratedService = GlobalPrefix + GeneratedNamespace + ".GeneratedService";
    public const string GlobalGeneratedServiceRegistry =
        GlobalPrefix + GeneratedNamespace + ".GeneratedServiceRegistry";
    public const string GlobalServiceRegistrationSink =
        GlobalPrefix + GeneratedNamespace + ".IRpcServiceRegistrationSink";
    public const string GlobalGeneratedServiceRegistrationSink =
        GlobalPrefix + GeneratedNamespace + ".IRpcGeneratedServiceRegistrationSink";

    public const string GlobalRpcPeer = GlobalPrefix + "DotBoxD.Services.Peer.RpcPeer";
    public const string GlobalRpcStreamHandle = GlobalPrefix + "DotBoxD.Services.Protocol.RpcStreamHandle";
    public const string GlobalRpcStreamKind = GlobalPrefix + "DotBoxD.Services.Protocol.RpcStreamKind";
    public const string GlobalServiceHandle = GlobalPrefix + "DotBoxD.Services.Protocol.ServiceHandle";
    public const string GlobalInstanceRegistry = GlobalPrefix + "DotBoxD.Services.Server.IInstanceRegistry";
    public const string GlobalRpcInvoker = GlobalPrefix + "DotBoxD.Services.Server.IRpcInvoker";
    public const string GlobalServiceDispatcher = GlobalPrefix + "DotBoxD.Services.Server.IServiceDispatcher";
    public const string GlobalNonStreamingServiceDispatcher =
        GlobalPrefix + "DotBoxD.Services.Server.INonStreamingServiceDispatcher";
    public const string GlobalSerializer = GlobalPrefix + "DotBoxD.Services.Serialization.ISerializer";
    public const string GlobalRpcStreamAttachment =
        GlobalPrefix + "DotBoxD.Services.Streaming.Frames.RpcStreamAttachment";
    public const string GlobalRpcStreamingContext =
        GlobalPrefix + "DotBoxD.Services.Streaming.Remote.RpcStreamingContext";
    public const string GlobalRpcStreamingContextInterface =
        GlobalPrefix + "DotBoxD.Services.Streaming.Remote.IRpcStreamingContext";
    public const string GlobalServiceNotFoundException =
        GlobalPrefix + "DotBoxD.Services.Exceptions.ServiceNotFoundException";
    public const string GlobalServiceNotFoundKind = GlobalServiceNotFoundException + ".NotFoundKind";
    public const string GlobalServiceProtocolException =
        GlobalPrefix + "DotBoxD.Services.Exceptions.ServiceProtocolException";

    public static string ArrayOf(string typeName) => typeName + "[]";

    public static string Generic(string typeName, string typeArgument)
        => typeName + "<" + typeArgument + ">";

    public static string Generic(string typeName, string firstTypeArgument, string secondTypeArgument)
        => typeName + "<" + firstTypeArgument + ", " + secondTypeArgument + ">";

    public static string NullableOf(string typeName) => typeName + "?";

    public static bool IsRpcServiceAttribute(string? typeName) =>
        typeName is RpcServiceAttribute or DotBoxDServiceAttribute;

    public static bool IsRpcMethodAttribute(string? typeName) =>
        typeName is RpcMethodAttribute or DotBoxDMethodAttribute;
}
