using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncReceiverResolver
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
        if (semanticType is not null && TryResolveWorld(semanticType, out worldType))
        {
            receiverType = TypeName(semanticType);
            return true;
        }

        if (semanticType is not null &&
            IsGeneratedServerInterfaceCandidate(semanticType) &&
            InvokeAsyncGeneratedServerInterfaceResolver.TryResolve(
                model.Compilation,
                semanticType,
                cancellationToken,
                out receiverType,
                out serverAccessType,
                out worldType))
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

    internal static bool IsGeneratedServerInterfaceNameCandidate(string name)
        => name.Length > "IServer".Length &&
           name.StartsWith("I", StringComparison.Ordinal) &&
           name.EndsWith("Server", StringComparison.Ordinal);

    private static bool IsGeneratedServerInterfaceCandidate(INamedTypeSymbol type)
        => type.TypeKind is TypeKind.Interface or TypeKind.Error &&
           IsGeneratedServerInterfaceNameCandidate(type.Name);

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

        foreach (var candidate in type.Interfaces)
        {
            if (HasDotBoxDServiceAttribute(candidate))
            {
                worldType = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDGenerationNames.Metadata.GeneratePluginServerAttribute);

    private static bool HasDotBoxDServiceAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDGenerationNames.Metadata.DotBoxDServiceAttribute);

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

    private static string PluginServerInterfaceTypeName(INamedTypeSymbol worldType)
        => "global::DotBoxD.Abstractions.IPluginServer<" + TypeName(worldType) + ">";

    private static string ServerInterfaceTypeName(INamedTypeSymbol facadeType, INamedTypeSymbol worldType)
    {
        var ns = facadeType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : facadeType.ContainingNamespace.ToDisplayString() + ".";
        return "global::" + ns + ServerInterfaceName(worldType);
    }

    private static string ServerInterfaceName(INamedTypeSymbol worldType)
    {
        var name = worldType.Name;
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        if (name.EndsWith("Access", StringComparison.Ordinal) && name.Length > "Access".Length)
        {
            name = name.Substring(0, name.Length - "Access".Length);
        }

        return "I" + name + "Server";
    }

    private static string TypeName(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
