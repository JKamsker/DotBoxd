using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginSymbolReader
{
    public static string? PluginId(IReadOnlyList<AttributeData> attributes)
    {
        for (var i = 0; i < attributes.Count; i++)
        {
            var attribute = attributes[i];
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.PluginAttribute,
                    StringComparison.Ordinal) ||
                string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.EventKernelAttribute,
                    StringComparison.Ordinal))
            {
                return attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
            }
        }

        return null;
    }

    public static IReadOnlyList<INamedTypeSymbol> EventTypes(INamedTypeSymbol kernelType)
    {
        var eventTypes = new List<INamedTypeSymbol>();
        foreach (var implementedInterface in kernelType.AllInterfaces)
        {
            if (IsEventKernelInterface(implementedInterface) &&
                implementedInterface.TypeArguments.Length > 0 &&
                implementedInterface.TypeArguments[0] is INamedTypeSymbol eventType)
            {
                eventTypes.Add(eventType);
            }
        }

        return eventTypes;
    }

    private static bool IsEventKernelInterface(INamedTypeSymbol type)
        => string.Equals(
            type.OriginalDefinition.ToDisplayString(),
            DotBoxDMetadataNames.EventKernelInterface,
            StringComparison.Ordinal);

    public static EquatableArray<EventPropertyModel> EventProperties(INamedTypeSymbol eventType)
    {
        var properties = PluginEventPropertyReader.Read(eventType);
        if (properties.Length == 0)
        {
            return default;
        }

        var models = new EventPropertyModel[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (PolymorphicHandleMetadataReader.TryResolve(property.Type, out var handle))
            {
                models[i] = new EventPropertyModel(
                    property.Name,
                    handle.KeyManifestTag,
                    handle.KeySandboxTypeSource,
                    Capability(property));
                continue;
            }

            // The full marshaller-eligible set (scalars + Guid + enum + list/array + Dictionary + DTO record) is
            // classified here, not just the 5 scalars: a non-scalar property becomes a real tag plus the C#
            // SandboxType the kernel parameter declares, so a thin event carrying a Guid id (or richer payload)
            // is no longer rejected wholesale. Genuinely unmarshallable types stay 'unsupported' and fail safe.
            var source = SandboxTypeSourceEmitter.TryEmit(property.Type);
            models[i] = new EventPropertyModel(
                property.Name,
                SandboxTypeSourceEmitter.ManifestTag(property.Type),
                source ?? string.Empty,
                Capability(property));
        }

        return EquatableArray<EventPropertyModel>.FromOwned(models);
    }

    private static string? Capability(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.CapabilityAttribute,
                    StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string { Length: > 0 } id)
            {
                return id;
            }
        }

        return null;
    }

    public static EquatableArray<LiveSettingModel> LiveSettings(
        INamedTypeSymbol kernelType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var count = CountLiveSettingProperties(kernelType);
        if (count == 0)
        {
            return default;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var settings = new LiveSettingModel[count];
        var index = 0;
        foreach (var property in LiveSettingProperties(kernelType))
        {
            if (!names.Add(property.Name))
            {
                throw new NotSupportedException($"Live setting '{property.Name}' is declared more than once.");
            }

            settings[index] = ToLiveSetting(property, semanticModel, cancellationToken);
            index++;
        }

        return EquatableArray<LiveSettingModel>.FromOwned(settings);
    }

    private static IEnumerable<IPropertySymbol> LiveSettingProperties(INamedTypeSymbol kernelType)
    {
        for (var current = kernelType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property && IsLiveSetting(property))
                {
                    ValidateLiveSettingProperty(property);
                    yield return property;
                }
            }
        }
    }

    private static int CountLiveSettingProperties(INamedTypeSymbol kernelType)
    {
        var count = 0;
        foreach (var property in LiveSettingProperties(kernelType))
        {
            _ = property;
            count++;
        }

        return count;
    }

    private static bool IsLiveSetting(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.LiveSettingAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateLiveSettingProperty(IPropertySymbol property)
    {
        if (property.DeclaredAccessibility != Accessibility.Public ||
            property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.GetMethod is null ||
            property.SetMethod is null ||
            property.GetMethod.DeclaredAccessibility != Accessibility.Public ||
            property.SetMethod.DeclaredAccessibility != Accessibility.Public ||
            property.SetMethod.IsInitOnly)
        {
            throw new NotSupportedException(
                $"Live setting '{property.Name}' must be a public instance property with public get and set accessors.");
        }
    }

    private static LiveSettingModel ToLiveSetting(
        IPropertySymbol property,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var syntax = DeclaringPropertySyntax(property, cancellationToken);
        var type = DotBoxDTypeNameReader.SandboxTypeName(property.Type);
        var range = PluginLiveSettingRangeReader.Read(property, type);
        return new LiveSettingModel(
            property.Name,
            type,
            LiveSettingSymbolKey(property),
            LiteralReader.DefaultValue(property.Type, syntax?.Initializer?.Value, semanticModel, cancellationToken),
            range.Min,
            range.Max);
    }

    private static string LiveSettingSymbolKey(IPropertySymbol property)
        => property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." + property.MetadataName;

    private static PropertyDeclarationSyntax? DeclaringPropertySyntax(
        IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax syntax)
            {
                return syntax;
            }
        }

        return null;
    }

}
