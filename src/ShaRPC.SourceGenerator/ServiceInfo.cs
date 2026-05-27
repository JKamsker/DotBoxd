namespace ShaRPC.SourceGenerator;

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
    /// <summary>Non-generic <see cref="System.Threading.Tasks.ValueTask"/> — async, no payload.</summary>
    ValueTask,
    /// <summary><see cref="System.Threading.Tasks.ValueTask{TResult}"/> — async with payload.</summary>
    ValueTaskOf,
}

/// <summary>
/// Immutable, value-equatable representation of a ShaRPC service.
/// </summary>
internal sealed record ServiceModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    EquatableArray<MethodModel> Methods);

/// <summary>
/// Immutable, value-equatable representation of a service method. When
/// <see cref="UnsupportedReason"/> is non-null the method shape cannot be marshalled
/// over RPC, but the proxy class still has to implement the interface — so the proxy
/// emits a throwing stub and the dispatcher omits a switch case.
/// </summary>
internal sealed record MethodModel(
    string Name,
    string RpcName,
    MethodReturnKind ReturnKind,
    string? UnwrappedReturnType,
    bool HasCancellationToken,
    EquatableArray<ParameterModel> Parameters,
    string? UnsupportedReason = null);

/// <summary>
/// Immutable, value-equatable representation of a method parameter (excluding any
/// <see cref="System.Threading.CancellationToken"/>, which is tracked separately on the method).
/// <see cref="RefKindKeyword"/> holds the C# modifier text (<c>""</c>, <c>"ref "</c>,
/// <c>"in "</c>, or <c>"out "</c>) — non-empty values appear only on parameters of
/// unsupported methods, which are emitted as throwing stubs.
/// </summary>
internal sealed record ParameterModel(string Name, string Type, string RefKindKeyword = "");

/// <summary>
/// Shared helpers used by both the proxy and dispatcher emitters.
/// </summary>
internal static class NamingHelpers
{
    /// <summary>
    /// Strips a leading <c>I</c> if it is followed by an uppercase letter (the C# convention
    /// for interface names). Avoids accidentally stripping the <c>I</c> from names like
    /// <c>Identity</c> or <c>Internal</c>.
    /// </summary>
    public static string StripInterfacePrefix(string interfaceName)
    {
        if (interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]))
        {
            return interfaceName.Substring(1);
        }

        return interfaceName;
    }

    /// <summary>
    /// Reconstructs the literal return-type text as it would appear on the user's interface
    /// declaration, so the generated proxy signature exactly matches.
    /// </summary>
    public static string GetDeclaredReturnTypeText(MethodReturnKind kind, string? unwrappedReturnType)
    {
        return kind switch
        {
            MethodReturnKind.Void => "void",
            MethodReturnKind.Sync => unwrappedReturnType!,
            MethodReturnKind.Task => "global::System.Threading.Tasks.Task",
            MethodReturnKind.TaskOf => $"global::System.Threading.Tasks.Task<{unwrappedReturnType}>",
            MethodReturnKind.ValueTask => "global::System.Threading.Tasks.ValueTask",
            MethodReturnKind.ValueTaskOf => $"global::System.Threading.Tasks.ValueTask<{unwrappedReturnType}>",
            _ => "void",
        };
    }

    /// <summary>
    /// Returns true if the return kind represents an asynchronous return that should be
    /// awaited and emitted with the <c>async</c> keyword.
    /// </summary>
    public static bool IsAsync(MethodReturnKind kind) =>
        kind == MethodReturnKind.Task ||
        kind == MethodReturnKind.TaskOf ||
        kind == MethodReturnKind.ValueTask ||
        kind == MethodReturnKind.ValueTaskOf;

    /// <summary>
    /// Returns true if the return kind carries a response payload (a generic Task/ValueTask of T
    /// or a synchronous T).
    /// </summary>
    public static bool HasReturnValue(MethodReturnKind kind) =>
        kind == MethodReturnKind.Sync ||
        kind == MethodReturnKind.TaskOf ||
        kind == MethodReturnKind.ValueTaskOf;
}
