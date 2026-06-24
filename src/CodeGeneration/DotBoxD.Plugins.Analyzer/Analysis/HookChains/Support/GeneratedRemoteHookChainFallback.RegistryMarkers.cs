using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static GeneratedRemoteHookChainTarget? TargetFromRegistryMarker(
        INamedTypeSymbol registryType,
        Compilation compilation)
    {
        var marker = SingleRegistryMarker(registryType, compilation);
        if (marker is null ||
            marker.ConstructorArguments.Length != 3 ||
            marker.ConstructorArguments[1].Value is not INamedTypeSymbol serverType ||
            marker.ConstructorArguments[2].Value is not INamedTypeSymbol contextType ||
            RegistryKind(marker) is not { } kind ||
            !MarkerOwnershipMatches(registryType, serverType, contextType, kind, compilation) ||
            !RegistryOnShapeMatches(registryType, contextType, kind))
        {
            return null;
        }

        return new GeneratedRemoteHookChainTarget(
            kind,
            contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static AttributeData? SingleRegistryMarker(INamedTypeSymbol registryType, Compilation compilation)
    {
        AttributeData? marker = null;
        foreach (var attribute in registryType.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, RegistryAttributeName, out _))
            {
                continue;
            }

            if (marker is not null)
            {
                return null;
            }

            marker = attribute;
        }

        return marker;
    }

    private static GeneratedRemoteHookChainKind? RegistryKind(AttributeData marker)
        => marker.ConstructorArguments[0].Value switch
        {
            0 => GeneratedRemoteHookChainKind.Hook,
            1 => GeneratedRemoteHookChainKind.Subscription,
            _ => null
        };

    private static bool MarkerOwnershipMatches(
        INamedTypeSymbol registryType,
        INamedTypeSymbol serverType,
        INamedTypeSymbol contextType,
        GeneratedRemoteHookChainKind kind,
        Compilation compilation)
    {
        if (serverType.TypeKind != TypeKind.Class ||
            !ContextMatchesGeneratedServer(serverType, contextType, compilation))
        {
            return false;
        }

        var propertyName = kind == GeneratedRemoteHookChainKind.Hook
            ? "Hooks"
            : "Subscriptions";
        foreach (var property in serverType.GetMembers(propertyName).OfType<IPropertySymbol>())
        {
            if (SymbolEqualityComparer.Default.Equals(property.Type, registryType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContextMatchesGeneratedServer(
        INamedTypeSymbol serverType,
        INamedTypeSymbol contextType,
        Compilation compilation)
        => GeneratePluginServerAttribute(serverType, compilation)?
            .NamedArguments
            .FirstOrDefault(static argument => string.Equals(argument.Key, "Context", StringComparison.Ordinal))
            .Value.Value is INamedTypeSymbol declaredContext &&
            SymbolEqualityComparer.Default.Equals(declaredContext, contextType);

    private static bool RegistryOnShapeMatches(
        INamedTypeSymbol registryType,
        INamedTypeSymbol contextType,
        GeneratedRemoteHookChainKind kind)
    {
        var expectedOriginal = kind == GeneratedRemoteHookChainKind.Hook
            ? DotBoxDGenerationNames.TypeNames.RemoteHookPipelineWithContextOriginal
            : DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineWithContextOriginal;
        foreach (var member in registryType.GetMembers("On").OfType<IMethodSymbol>())
        {
            if (member.Arity != 1 ||
                !SymbolEqualityComparer.Default.Equals(member.ContainingType, registryType) ||
                member.Parameters.Length != 0 ||
                member.ReturnType is not INamedTypeSymbol returnType ||
                !string.Equals(returnType.OriginalDefinition.ToDisplayString(), expectedOriginal, StringComparison.Ordinal) ||
                returnType.TypeArguments.Length != 2 ||
                !SymbolEqualityComparer.Default.Equals(returnType.TypeArguments[0], member.TypeParameters[0]) ||
                !SymbolEqualityComparer.Default.Equals(returnType.TypeArguments[1], contextType))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
