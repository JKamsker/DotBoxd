using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

/// <summary>
/// Resolves the reverse server-&gt;plugin event-callback contract for a generated plugin facade, discovered by
/// the same <c>{worldNs}.Ipc</c> convention the factory uses for the control service. Optional: a world with no
/// such contract keeps the original facade (no local handlers). Two guards keep the emitted facade compilable:
/// a shape guard requires the <c>[DotBoxDService]</c> interface to carry the expected
/// <c>OnEventAsync(string, byte[], CancellationToken) -&gt; ValueTask</c>/<c>ValueTask&lt;T&gt;</c> method, and a transport guard requires
/// the compilation to actually expose the generated <c>DotBoxDGeneratedExtensions.Provide{suffix}</c> extension
/// (absent or ambiguous — e.g. a test stub — falls back to the original wiring instead of a dangling call).
/// </summary>
internal static class PluginServerEventCallbackResolver
{
    public static (INamedTypeSymbol Type, string ProvideSuffix, ITypeSymbol ReturnType, bool ReturnHasValue)? Resolve(
        Compilation compilation,
        INamedTypeSymbol worldType,
        CancellationToken cancellationToken)
    {
        var callback = ResolveContract(compilation, worldType);
        if (callback is null)
        {
            return null;
        }

        var suffix = PluginServerWorldExtensionSuffixResolver.Resolve(compilation, callback.Value.Type, cancellationToken);
        return HasProvideExtension(compilation, suffix, callback.Value.Type)
            ? (callback.Value.Type, suffix, callback.Value.Method.ReturnType, ReturnHasValue(callback.Value.Method.ReturnType))
            : null;
    }

    private static (INamedTypeSymbol Type, IMethodSymbol Method)? ResolveContract(
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
        var callback = compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IPluginEventCallback");
        if (callback is null ||
            callback.TypeKind != TypeKind.Interface ||
            !HasDotBoxDServiceAttribute(callback))
        {
            return null;
        }

        var method = ResolveEventCallbackMethod(callback);
        return method is null ? null : (callback, method);
    }

    private static IMethodSymbol? ResolveEventCallbackMethod(INamedTypeSymbol callback)
    {
        foreach (var member in callback.GetMembers("OnEventAsync"))
        {
            if (member is IMethodSymbol { Parameters.Length: 3 } method &&
                IsValueTask(method.ReturnType) &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                method.Parameters[1].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } &&
                string.Equals(
                    method.Parameters[2].Type.ToDisplayString(),
                    "System.Threading.CancellationToken",
                    StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    private static bool IsValueTask(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            Name: "ValueTask",
            ContainingNamespace: { } ns
        } named &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal) &&
        (!named.IsGenericType || named.TypeArguments.Length == 1);

    private static bool ReturnHasValue(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true };

    // The Provide{suffix} extension is generated into DotBoxDGeneratedExtensions by the services source
    // generator in the assembly that declares the contract. GetTypeByMetadataName returns null when that type is
    // absent or ambiguous (defined in both source and a reference), so a test that stubs only the Get* members
    // falls back to the original wiring rather than referencing a method that does not exist.
    private static bool HasProvideExtension(
        Compilation compilation,
        string provideSuffix,
        INamedTypeSymbol callbackType)
    {
        var extensions = compilation.GetTypeByMetadataName("DotBoxD.Services.Generated.DotBoxDGeneratedExtensions");
        var rpcPeerType = compilation.GetTypeByMetadataName("DotBoxD.Services.Peer.RpcPeer");
        if (extensions is null || rpcPeerType is null)
        {
            return false;
        }

        foreach (var member in extensions.GetMembers("Provide" + provideSuffix))
        {
            if (member is IMethodSymbol method &&
                IsProvideExtensionMethod(method, rpcPeerType, callbackType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProvideExtensionMethod(
        IMethodSymbol method,
        INamedTypeSymbol rpcPeerType,
        INamedTypeSymbol callbackType)
        => method is { IsStatic: true, IsGenericMethod: false, Parameters.Length: 2 } &&
        SymbolEqualityComparer.Default.Equals(method.ReturnType, rpcPeerType) &&
        SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, rpcPeerType) &&
        SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, callbackType);

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
