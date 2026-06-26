using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static PluginServerControlProperty[] ResolveControls(
        INamedTypeSymbol worldType,
        CancellationToken cancellationToken)
    {
        var controls = new List<PluginServerControlProperty>();
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        var fieldNames = new HashSet<string>(ReservedFacadeFieldNames(), StringComparer.Ordinal);
        var accumulatorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(worldType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol
                {
                    IsStatic: false,
                    GetMethod: not null,
                    SetMethod: null,
                    Type: INamedTypeSymbol propertyType
                } property ||
                !HasAttribute(propertyType, DotBoxDMetadataNames.DotBoxDServiceAttribute) ||
                !seenProperties.Add(property.Name))
            {
                continue;
            }

            var serviceWrappers = new Dictionary<string, ServiceWrapperBuilder>(StringComparer.Ordinal);
            controls.Add(new PluginServerControlProperty(
                property.Name,
                UniqueFieldName(property.Name, fieldNames),
                TypeName(propertyType),
                PluginServerXmlDocumentation.FromSymbol(
                    property,
                    "Accesses the server's " + property.Name + " domain control after StartAsync.",
                    cancellationToken),
                property.Name + "PluginControl",
                UniqueTypeName(property.Name + "Accumulator", accumulatorNames),
                new EquatableArray<PluginServerForwardedProperty>(
                    ResolveForwardedProperties(
                        propertyType,
                        serviceWrappers,
                        skipServiceProperties: false,
                        cancellationToken)),
                new EquatableArray<PluginServerForwardedMethod>(
                    ResolveMethods(
                        propertyType,
                        serviceWrappers,
                        cancellationToken)),
                new EquatableArray<PluginServerServiceWrapper>(
                    serviceWrappers.Values
                        .Select(static wrapper => new PluginServerServiceWrapper(
                            wrapper.Type,
                            wrapper.WrapperName,
                            wrapper.Documentation,
                            new EquatableArray<PluginServerForwardedProperty>(wrapper.Properties.ToArray()),
                            new EquatableArray<PluginServerForwardedMethod>(wrapper.Methods.ToArray())))
                        .ToArray())));
        }

        return controls.ToArray();
    }

    private static string[] ReservedFacadeFieldNames()
        =>
        [
            "_connectionFactory",
            "_anonymousKernels",
            "_serverExtensions",
            "_setupInstalls",
            "_control",
            "_world",
            "_hooks",
            "_subscriptions",
            "_localHandlers",
            "_session",
            "_started",
            "_setupReplayed",
            "_setupReplayIndex",
            "_configured",
            "_disposed",
        ];

    private static string UniqueFieldName(string propertyName, HashSet<string> used)
    {
        var baseName = "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        if (used.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = baseName + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string UniqueTypeName(string baseName, HashSet<string> used)
    {
        if (used.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
