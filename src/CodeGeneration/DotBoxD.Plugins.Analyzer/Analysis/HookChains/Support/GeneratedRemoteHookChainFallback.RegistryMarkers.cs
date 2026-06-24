using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static GeneratedRemoteHookChainTarget? TargetFromRegistryMarker(INamedTypeSymbol registryType)
    {
        var marker = SingleRegistryMarker(registryType);
        if (marker is null ||
            marker.ConstructorArguments.Length != 3 ||
            marker.ConstructorArguments[1].Value is not INamedTypeSymbol serverType ||
            marker.ConstructorArguments[2].Value is not INamedTypeSymbol contextType ||
            RegistryKind(marker) is not { } kind ||
            !MarkerOwnershipMatches(registryType, serverType, contextType, kind))
        {
            return null;
        }

        return new GeneratedRemoteHookChainTarget(
            kind,
            contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static AttributeData? SingleRegistryMarker(INamedTypeSymbol registryType)
    {
        AttributeData? marker = null;
        foreach (var attribute in registryType.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), RegistryAttributeName, StringComparison.Ordinal))
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
        GeneratedRemoteHookChainKind kind)
    {
        if (serverType.TypeKind != TypeKind.Class ||
            !ContextMatchesGeneratedServer(serverType, contextType))
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

    private static bool ContextMatchesGeneratedServer(INamedTypeSymbol serverType, INamedTypeSymbol contextType)
        => GeneratePluginServerAttribute(serverType)?
            .NamedArguments
            .FirstOrDefault(static argument => string.Equals(argument.Key, "Context", StringComparison.Ordinal))
            .Value.Value is INamedTypeSymbol declaredContext &&
            SymbolEqualityComparer.Default.Equals(declaredContext, contextType);
}
