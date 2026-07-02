using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncReceiverResolver
{
    public static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;

        var semanticType = model.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
        if (semanticType is not null && TryResolveGeneratedFacadeType(semanticType, out receiverType, out serverAccessType, out worldType))
        {
            return true;
        }

        if (semanticType is not null &&
            IsGeneratedServerInterfaceCandidate(semanticType) &&
            (TryResolveGeneratedServerShape(semanticType, out receiverType, out serverAccessType, out worldType) ||
             InvokeAsyncGeneratedServerInterfaceResolver.TryResolve(
                 model.Compilation,
                 semanticType,
                 cancellationToken,
                 out receiverType,
                 out serverAccessType,
                 out worldType)))
        {
            return true;
        }

        if (InvokeAsyncGeneratedBuilderResolver.TryResolve(
                model,
                receiver,
                cancellationToken,
                out var generatedType) &&
            TryResolveWorld(generatedType, out worldType))
        {
            receiverType = PluginServerInterfaceTypeName(worldType);
            serverAccessType = ServerInterfaceTypeName(generatedType, worldType);
            return true;
        }

        return TryResolveGeneratedServicesReceiver(
            model,
            receiver,
            cancellationToken,
            out receiverType,
            out serverAccessType,
            out worldType);
    }

    internal static bool TryResolveGeneratedFacadeType(
        INamedTypeSymbol type,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        if (TryResolveWorld(type, out worldType))
        {
            receiverType = TypeName(type);
            return true;
        }

        if (!TryResolveGeneratedFacadeBase(type, out var facadeBaseType, out worldType))
        {
            return false;
        }

        receiverType = TypeName(facadeBaseType);
        return true;
    }

    private static bool TryResolveGeneratedFacadeBase(
        INamedTypeSymbol type,
        out INamedTypeSymbol worldType)
        => TryResolveGeneratedFacadeBase(type, out _, out worldType);

    private static bool TryResolveGeneratedFacadeBase(
        INamedTypeSymbol type,
        out INamedTypeSymbol facadeBaseType,
        out INamedTypeSymbol worldType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (TryResolveWorld(current, out worldType))
            {
                facadeBaseType = current;
                return true;
            }
        }

        facadeBaseType = null!;
        worldType = null!;
        return false;
    }

    internal static bool IsGeneratedServerInterfaceNameCandidate(string name)
        => name.Length > "IServer".Length &&
           name.StartsWith("I", StringComparison.Ordinal) &&
           name.EndsWith("Server", StringComparison.Ordinal);

    private static bool IsGeneratedServerInterfaceCandidate(INamedTypeSymbol type)
        => IsGeneratedServerInterfaceNameCandidate(type.Name) &&
           (type.TypeKind == TypeKind.Error ||
            (type.TypeKind == TypeKind.Interface &&
             InheritsPluginServerInterface(type) &&
             HasGeneratedServerShape(type)));

    private static bool InheritsPluginServerInterface(INamedTypeSymbol type)
        => type.AllInterfaces.Any(static candidate => string.Equals(
            candidate.OriginalDefinition.ToDisplayString(),
            "DotBoxD.Abstractions.IPluginServer<TWorld>",
            StringComparison.Ordinal));

    private static bool TryResolveGeneratedServerShape(
        INamedTypeSymbol type,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
    {
        receiverType = string.Empty;
        serverAccessType = null;
        worldType = null!;
        if (!TryPluginServerWorld(type, out worldType))
        {
            return false;
        }

        receiverType = PluginServerInterfaceTypeName(worldType);
        serverAccessType = TypeName(type);
        return true;
    }

    private static bool TryPluginServerWorld(INamedTypeSymbol type, out INamedTypeSymbol worldType)
    {
        foreach (var candidate in type.AllInterfaces)
        {
            if (string.Equals(
                    candidate.OriginalDefinition.ToDisplayString(),
                    "DotBoxD.Abstractions.IPluginServer<TWorld>",
                    StringComparison.Ordinal) &&
                candidate.TypeArguments.Length == 1 &&
                candidate.TypeArguments[0] is INamedTypeSymbol named)
            {
                worldType = named;
                return true;
            }
        }

        worldType = null!;
        return false;
    }

    private static bool HasGeneratedServerShape(INamedTypeSymbol type)
        => type.GetMembers("Services").OfType<IPropertySymbol>().Any(property =>
               SymbolEqualityComparer.Default.Equals(property.Type, type)) &&
           type.GetMembers("WireClient").OfType<IPropertySymbol>().Any() &&
           type.GetMembers("EnsureAnonymousKernelAsync").OfType<IMethodSymbol>().Any();

    private static bool TryResolveWorld(
        INamedTypeSymbol type,
        out INamedTypeSymbol worldType)
    {
        worldType = null!;
        if (!HasGeneratePluginServerAttribute(type))
        {
            return false;
        }

        var found = false;
        foreach (var candidate in type.Interfaces)
        {
            if (HasDotBoxDServiceAttribute(candidate))
            {
                if (found)
                {
                    worldType = null!;
                    return false;
                }

                worldType = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
        => InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(type);

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
        => InvokeAsyncAttributeMatcher.HasRpcServiceAttribute(type);
}
