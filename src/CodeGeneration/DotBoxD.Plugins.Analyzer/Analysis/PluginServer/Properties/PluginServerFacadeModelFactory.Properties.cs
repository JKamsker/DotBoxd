using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static PluginServerForwardedProperty[] ResolveForwardedProperties(
        INamedTypeSymbol serviceType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        bool skipServiceProperties,
        CancellationToken cancellationToken)
    {
        var properties = new List<PluginServerForwardedProperty>();
        var seenProperties = new Dictionary<string, PluginServerForwardedProperty>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(serviceType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol property ||
                IsControlPlaneMember(property.ContainingType))
            {
                continue;
            }

            ValidateForwardedProperty(property);
            var returnWrapperName = ReturnPropertyWrapper(property.Type, serviceWrappers, skipServiceProperties, cancellationToken);
            if (returnWrapperName.IsSkipped)
            {
                continue;
            }

            var forwarded = new PluginServerForwardedProperty(
                property.Name,
                TypeName(property.Type),
                PluginServerFlowAttributeSource.PropertyAttributes(property),
                PluginServerXmlDocumentation.FromSymbol(
                    property,
                    "Forwards the " + property.Name + " property from the remote domain service.",
                    cancellationToken),
                returnWrapperName.Name);
            if (seenProperties.TryGetValue(property.Name, out var existing))
            {
                if (!string.Equals(existing.Type, forwarded.Type, StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Generated plugin server member '{property.ToDisplayString()}' has an inherited property collision with a different type.");
                }

                if (!existing.Attributes.Equals(forwarded.Attributes))
                {
                    throw new NotSupportedException(
                        $"Generated plugin server member '{property.ToDisplayString()}' has an inherited property collision with different flow attributes.");
                }

                continue;
            }

            seenProperties.Add(property.Name, forwarded);
            properties.Add(forwarded);
        }

        return properties.ToArray();
    }

    private static void ValidateForwardedProperty(IPropertySymbol property)
    {
        if (property.DeclaredAccessibility != Accessibility.Public)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{property.ToDisplayString()}' is interface property '{property.Name}' with non-public access; generated plugin server facades may forward public get-only properties only.");
        }

        if (property.GetMethod is not null &&
            property.GetMethod.DeclaredAccessibility != Accessibility.Public)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{property.ToDisplayString()}' is interface property '{property.Name}' with a non-public getter; generated plugin server facades may forward public get-only properties only.");
        }

        if (property.IsStatic ||
            property.GetMethod is null ||
            property.SetMethod is not null ||
            property.Parameters.Length != 0)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{property.ToDisplayString()}' must be a get-only instance property.");
        }

        if (property.RefKind != RefKind.None)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{property.ToDisplayString()}' must not declare ref returns.");
        }
    }

    private static ServicePropertyWrapper ReturnPropertyWrapper(
        ITypeSymbol returnType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        bool skipServiceProperties,
        CancellationToken cancellationToken)
    {
        if (returnType is not INamedTypeSymbol namedReturnType ||
            !HasAttribute(namedReturnType, DotBoxDMetadataNames.DotBoxDServiceAttribute))
        {
            return new ServicePropertyWrapper(null);
        }

        return skipServiceProperties
            ? new ServicePropertyWrapper(null, IsSkipped: true)
            : new ServicePropertyWrapper(
                EnsureServiceWrapper(namedReturnType, serviceWrappers, cancellationToken),
                IsSkipped: false);
    }

    private readonly record struct ServicePropertyWrapper(string? Name, bool IsSkipped = false);
}
