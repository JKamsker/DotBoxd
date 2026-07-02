using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

/// <summary>
/// Classifies the return shape of an RPC-facing method as declared on the user's interface.
/// </summary>
internal enum MethodReturnKind
{
    /// <summary><c>void</c></summary>
    Void,
    /// <summary>A non-<see cref="System.Threading.Tasks.Task"/> / non-<see cref="System.Threading.Tasks.ValueTask"/> return — synchronous T.</summary>
    Sync,
    /// <summary>Non-generic <see cref="System.Threading.Tasks.Task"/> — async, no payload.</summary>
    Task,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> — async with payload.</summary>
    TaskOf,
    /// <summary>A synchronous <c>[DotBoxDService]</c> interface return — nested sub-service.</summary>
    SyncSubService,
    /// <summary>Non-generic <see cref="System.Threading.Tasks.ValueTask"/> — async, no payload.</summary>
    ValueTask,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> — async with payload.</summary>
    ValueTaskOf,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> where <c>TResult</c> is itself a <c>[DotBoxDService]</c> interface — nested sub-service.</summary>
    TaskOfSubService,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> where <c>TResult</c> is itself a <c>[DotBoxDService]</c> interface — nested sub-service.</summary>
    ValueTaskOfSubService,
    /// <summary><c>IAsyncEnumerable&lt;T&gt;</c> streamed item-by-item.</summary>
    AsyncEnumerable,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> whose result is <c>IAsyncEnumerable&lt;T&gt;</c>.</summary>
    TaskOfAsyncEnumerable,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> whose result is <c>IAsyncEnumerable&lt;T&gt;</c>.</summary>
    ValueTaskOfAsyncEnumerable,
    /// <summary><see cref="System.IO.Stream"/> streamed as bytes.</summary>
    Stream,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> whose result is <see cref="System.IO.Stream"/>.</summary>
    TaskOfStream,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> whose result is <see cref="System.IO.Stream"/>.</summary>
    ValueTaskOfStream,
    /// <summary><c>Pipe</c> streamed as bytes.</summary>
    Pipe,
    /// <summary><see cref="System.Threading.Tasks.Task{TResult}"/> whose result is <c>Pipe</c>.</summary>
    TaskOfPipe,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> whose result is <c>Pipe</c>.</summary>
    ValueTaskOfPipe,
}

internal enum ParameterStreamKind
{
    None,
    Stream,
    Pipe,
    AsyncEnumerable,
}

/// <summary>
/// Information needed to wire a method returning a nested sub-service: the fully-qualified
/// interface name (so the proxy can construct a sibling proxy) and the RPC service name
/// (so the wire instance dispatch hits the right registry slot).
/// </summary>
internal sealed record SubServiceInfo(
    string QualifiedInterfaceName,
    string ServiceName,
    bool AllowsNull,
    bool HasProxyCompanion);

/// <summary>
/// Immutable, value-equatable representation of a DotBoxD service.
/// </summary>
internal sealed record ServiceModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    EquatableArray<MethodModel> Methods,
    EquatableArray<ServicePropertyModel> Properties,
    string RawServiceName = "");

/// <summary>Immutable, value-equatable representation of a get-only sub-service property.</summary>
internal sealed record ServicePropertyModel(
    string Name,
    string ImplementationType,
    string Type,
    string? ProxyType,
    bool IsInstanceId,
    SubServiceInfo? SubService);

/// <summary>
/// Immutable, value-equatable representation of a service method. When
/// <see cref="UnsupportedReason"/> is non-null the method shape cannot be marshalled
/// over RPC, but the proxy class still has to implement the interface — so the proxy
/// emits a throwing stub and the dispatcher omits a switch case.
/// </summary>
internal sealed record MethodModel(
    string Name,
    string ExplicitImplementationType,
    string RpcName,
    MethodReturnKind ReturnKind,
    string DeclaredReturnType,
    string? UnwrappedReturnType,
    string ReturnRefKindKeyword,
    string ReturnAttributePrefix,
    bool HasCancellationToken,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<string> AdditionalExplicitImplementationTypes,
    bool RequiresUnsafeSignature = false,
    int TypeParameterCount = 0,
    string TypeParameterList = "",
    string ConstraintClauses = "",
    bool RequiresDispatcherReceiverCast = false,
    string? UnsupportedReason = null,
    SubServiceInfo? SubService = null,
    string RawRpcName = "",
    string MetadataReturnType = "",
    string? MetadataResultType = null);

/// <summary>
/// Immutable, value-equatable representation of a method parameter.
/// <see cref="IsCancellationToken"/> marks parameters that are part of the user's
/// signature but are not serialized into the RPC payload.
/// <see cref="ScopeKeyword"/> preserves a user-authored <c>scoped</c> modifier.
/// <see cref="RefKindKeyword"/> holds the C# modifier text (<c>""</c>, <c>"ref "</c>,
/// <c>"in "</c>, or <c>"out "</c>).
/// <see cref="IsParams"/> preserves a user-authored <c>params</c> modifier for generated public
/// signatures when the parameter is still the final parameter.
/// <see cref="CallerInfoAttributePrefix"/> preserves compiler-recognized parameter attributes as
/// generated-source text, including a trailing space when non-empty.
/// <see cref="DefaultValueLiteral"/> holds the C# literal text of a non-cancellation-token
/// parameter's default value (e.g. <c>"\"x\""</c>, <c>"5"</c>, <c>"null"</c>, <c>"default"</c>),
/// so the generated proxy and async-sibling signatures preserve it; empty when there is no default
/// or it cannot be expressed as a literal. Cancellation-token defaults are emitted as
/// <c>= default</c>.
/// <see cref="MetadataDefaultValueExpression"/> holds the generated C# expression used for metadata
/// default values when it differs from the public signature literal.
/// </summary>
internal sealed record ParameterModel(
    string Name,
    string Type,
    string SignatureType,
    string RefKindKeyword = "",
    bool IsParams = false,
    bool IsCancellationToken = false,
    bool HasDefaultValue = false,
    string DefaultValueLiteral = "",
    string MetadataDefaultValueExpression = "",
    ParameterStreamKind StreamKind = ParameterStreamKind.None,
    string? StreamItemType = null,
    string MetadataType = "",
    string CallerInfoAttributePrefix = "",
    string ScopeKeyword = "");

/// <summary>
/// A <see cref="ServiceModel"/> paired with its computed async-sibling projection. Lives
/// as one value-equatable record so the per-service source-output step can be driven
/// from a single input without losing incrementality.
/// </summary>
internal sealed record ServiceBundle(
    ServiceModel Model,
    EquatableArray<AsyncSiblingMethod> SiblingMethods)
{
    public static ServiceBundle Empty(ServiceModel model) =>
        new(
            model,
            EquatableArray<AsyncSiblingMethod>.Empty);
}

internal sealed record ServiceProjection(
    ServiceBundle Bundle,
    EquatableArray<MethodDiagnostic> SiblingCollisions);

/// <summary>
/// Shape of one method as it should appear on the auto-generated async sibling interface.
/// </summary>
internal sealed record AsyncSiblingMethod(
    int SourceIndex,
    // Method name on the sibling (e.g. "Add" -> "AddAsync").
    string Name,
    // Original method this row was derived from — used by the proxy emitter to
    // pick the wire call shape and to suppress duplicate emission when the sibling row
    // is identical to the original method.
    MethodModel Source,
    // The return kind on the sibling — always Task / TaskOf / ValueTask / ValueTaskOf;
    // sync methods are projected onto MethodReturnKind.Task or
    // MethodReturnKind.TaskOf depending on whether they carry a payload.
    MethodReturnKind SiblingReturnKind,
    // Parameter list emitted on the sibling interface.
    EquatableArray<ParameterModel> Parameters,
    // True when this row materially differs from Source — i.e.
    // the proxy needs an extra method to satisfy the sibling interface. False when one
    // physical method on the proxy satisfies both interfaces (already-async methods
    // with the same name and signature).
    bool RequiresExtraProxyMethod);
