using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncReceiverResolver
{
    private const string BuilderSuffix = "Builder";

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

        if (TryResolveGeneratedBuilderLocal(model, receiver, cancellationToken, out var generatedType) &&
            TryResolveWorld(generatedType, out worldType))
        {
            receiverType = PluginServerInterfaceTypeName(worldType);
            serverAccessType = ServerInterfaceTypeName(generatedType, worldType);
            return true;
        }

        return false;
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

        if (!TryResolveGeneratedFacadeBase(type, out worldType))
        {
            return false;
        }

        receiverType = TypeName(type);
        return true;
    }

    private static bool TryResolveGeneratedFacadeBase(
        INamedTypeSymbol type,
        out INamedTypeSymbol worldType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (TryResolveWorld(current, out worldType))
            {
                return true;
            }
        }

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

    private static bool TryResolveGeneratedBuilderLocal(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (receiver is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                } &&
                TryFacadeNameFromBuilderInitializer(initializer, out var facadeName) &&
                TryFindGeneratedFacade(model.Compilation, facadeName, cancellationToken, out receiverType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFacadeNameFromBuilderInitializer(
        ExpressionSyntax initializer,
        out string facadeName)
    {
        facadeName = string.Empty;
        return initializer is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Build",
                Expression: { } buildReceiver
            }
        } && TryFacadeNameFromBuilderFactory(buildReceiver, out facadeName);
    }

    private static bool TryFacadeNameFromBuilderFactory(
        ExpressionSyntax buildReceiver,
        out string facadeName)
    {
        facadeName = string.Empty;
        var current = buildReceiver;
        while (current is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: { } next }
            })
        {
            if (TryFacadeNameFromBuilderType(next, out facadeName))
            {
                return true;
            }

            current = next;
        }

        return TryFacadeNameFromBuilderType(current, out facadeName);
    }

    private static bool TryFacadeNameFromBuilderType(
        ExpressionSyntax builderType,
        out string facadeName)
    {
        var builderName = builderType switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };

        if (!builderName.EndsWith(BuilderSuffix, StringComparison.Ordinal) ||
            builderName.Length == BuilderSuffix.Length)
        {
            facadeName = string.Empty;
            return false;
        }

        facadeName = builderName.Substring(0, builderName.Length - BuilderSuffix.Length);
        return true;
    }

    private static bool TryFindGeneratedFacade(
        Compilation compilation,
        string facadeName,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        foreach (var symbol in compilation.GetSymbolsWithName(
                     name => string.Equals(name, facadeName, StringComparison.Ordinal),
                     SymbolFilter.Type,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is INamedTypeSymbol candidate &&
                HasGeneratePluginServerAttribute(candidate))
            {
                receiverType = candidate;
                return true;
            }
        }

        return false;
    }

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
        => HasAttribute(type, DotBoxDMetadataNames.GeneratePluginServerAttribute);

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDMetadataNames.DotBoxDServiceAttribute);

    private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    metadataName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

}
