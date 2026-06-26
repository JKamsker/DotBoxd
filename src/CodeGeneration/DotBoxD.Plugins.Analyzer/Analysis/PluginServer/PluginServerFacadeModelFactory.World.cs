using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol type)
    {
        INamedTypeSymbol? worldType = null;
        foreach (var candidate in type.Interfaces)
        {
            if (!HasAttribute(candidate, DotBoxDMetadataNames.DotBoxDServiceAttribute))
            {
                continue;
            }

            if (worldType is not null)
            {
                throw new NotSupportedException(
                    $"Generated plugin server '{type.Name}' must directly implement one [DotBoxDService] world interface.");
            }

            worldType = candidate;
        }

        return worldType;
    }

    private static INamedTypeSymbol? ResolveControlService(
        INamedTypeSymbol serverType,
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var attribute = GeneratePluginServerAttribute(serverType);
        var explicitControlService = attribute is null ? null : ControlServiceType(attribute);
        if (explicitControlService is not null)
        {
            return explicitControlService;
        }

        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
        return compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IGamePluginControlService");
    }

    private static INamedTypeSymbol? ControlServiceType(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (!string.Equals(argument.Key, "ControlService", StringComparison.Ordinal))
            {
                continue;
            }

            if (argument.Value.Value is INamedTypeSymbol controlServiceType)
            {
                return controlServiceType;
            }

            if (argument.Value.Value is null)
            {
                // Explicit `ControlService = null` is equivalent to omitting it: fall back to the convention.
                return null;
            }

            throw new NotSupportedException("ControlService must be typeof(TControlService).");
        }

        return null;
    }

    private static ITypeSymbol? ResolveLiveSettingUpdateType(
        INamedTypeSymbol controlServiceType,
        CancellationToken cancellationToken)
    {
        foreach (var member in MembersIncludingInherited(controlServiceType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: false,
                    Name: "UpdateSettingsAsync"
                } method)
            {
                continue;
            }

            if (LiveSettingUpdateElementType(method) is { } elementType)
            {
                return elementType;
            }
        }

        return null;
    }

    // The live-setting update element type is the element type of UpdateSettingsAsync's update-batch array.
    // The conventional `updates` parameter wins when present; otherwise the method's single array parameter is
    // used, so an explicit control-plane contract is not forced to use a specific parameter name.
    private static ITypeSymbol? LiveSettingUpdateElementType(IMethodSymbol method)
    {
        IArrayTypeSymbol? fallback = null;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not IArrayTypeSymbol updateArray)
            {
                continue;
            }

            if (string.Equals(parameter.Name, "updates", StringComparison.Ordinal))
            {
                return updateArray.ElementType;
            }

            fallback ??= updateArray;
        }

        return fallback?.ElementType;
    }
}
