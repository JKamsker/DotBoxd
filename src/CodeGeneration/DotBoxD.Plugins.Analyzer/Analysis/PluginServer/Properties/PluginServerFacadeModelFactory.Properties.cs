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
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(serviceType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol property ||
                IsControlPlaneMember(property.ContainingType))
            {
                continue;
            }

            ValidateForwardedProperty(property);
            if (!seenProperties.Add(property.Name))
            {
                continue;
            }

            var returnWrapperName = ReturnPropertyWrapper(property.Type, serviceWrappers, skipServiceProperties, cancellationToken);
            if (returnWrapperName.IsSkipped)
            {
                continue;
            }

            properties.Add(new PluginServerForwardedProperty(
                property.Name,
                TypeName(property.Type),
                PluginServerXmlDocumentation.FromSymbol(
                    property,
                    "Forwards the " + property.Name + " property from the remote domain service.",
                    cancellationToken),
                returnWrapperName.Name));
        }

        return properties.ToArray();
    }

    private static void ValidateForwardedProperty(IPropertySymbol property)
    {
        if (property.IsStatic ||
            property.GetMethod is null ||
            property.SetMethod is not null ||
            property.Parameters.Length != 0)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{property.ToDisplayString()}' must be a get-only instance property.");
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
