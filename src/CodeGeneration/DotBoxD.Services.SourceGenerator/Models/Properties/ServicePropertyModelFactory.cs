using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ServicePropertyModelFactory
{
    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static UnsupportedMemberDiagnostic? Collect(
        IReadOnlyList<IPropertySymbol> interfaceProperties,
        List<ServicePropertyModel> properties,
        CancellationToken ct)
    {
        var seenPropertyTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var propertySymbol in interfaceProperties)
        {
            ct.ThrowIfCancellationRequested();
            var propertyName = IdentifierHelpers.EscapeIdentifier(propertySymbol.Name);
            var propertyTypeName = propertySymbol.Type.ToDisplayString(s_qualifiedFormat);
            if (seenPropertyTypes.TryGetValue(propertyName, out var existingPropertyType))
            {
                if (!string.Equals(existingPropertyType, propertyTypeName, StringComparison.Ordinal))
                {
                    return new UnsupportedMemberDiagnostic(
                        $"inherited property '{propertySymbol.Name}' has the same name as another property but an incompatible type",
                        DiagnosticLocationFactory.FromSymbol(propertySymbol));
                }

                continue;
            }

            seenPropertyTypes[propertyName] = propertyTypeName;
            AddProperty(properties, propertySymbol, propertyName, propertyTypeName);
        }

        return null;
    }

    private static void AddProperty(
        List<ServicePropertyModel> properties,
        IPropertySymbol propertySymbol,
        string propertyName,
        string propertyTypeName)
    {
        var declaringType = propertySymbol.ContainingType.ToDisplayString(s_qualifiedFormat);
        if (ServiceShapeValidator.IsInstanceIdProperty(propertySymbol))
        {
            properties.Add(new ServicePropertyModel(
                propertyName,
                propertyTypeName,
                ProxyType: null,
                IsInstanceId: true,
                DeclaringType: declaringType));
            return;
        }

        if (propertySymbol.Type is not INamedTypeSymbol propertyType)
        {
            return;
        }

        var propertyNamespace = GetNamespace(propertyType.ContainingNamespace);
        var proxyName = NamingHelpers.StripInterfacePrefix(propertyType.Name) + "Proxy";
        properties.Add(new ServicePropertyModel(
            propertyName,
            propertyTypeName,
            IdentifierHelpers.QualifyTypeName(propertyNamespace, proxyName),
            IsInstanceId: false,
            DeclaringType: declaringType));
    }

    private static string GetNamespace(INamespaceSymbol namespaceSymbol)
    {
        if (namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (var current = namespaceSymbol; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            parts.Push(current.Name);
        }

        return string.Join(".", parts);
    }
}
