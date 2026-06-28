using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class InheritedPropertyDeduplicator
{
    public static UnsupportedMemberDiagnostic? CollectUnique(
        IEnumerable<IPropertySymbol> properties,
        IEnumerable<IMethodSymbol> methods,
        List<IPropertySymbol> uniqueProperties,
        CancellationToken ct)
    {
        var seen = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            ct.ThrowIfCancellationRequested();

            if (!seen.TryGetValue(property.Name, out var existing))
            {
                seen.Add(property.Name, property);
                uniqueProperties.Add(property);
                continue;
            }

            if (!SymbolEqualityComparer.IncludeNullability.Equals(existing.Type, property.Type))
            {
                return new UnsupportedMemberDiagnostic(
                    $"inherited property '{property.Name}' has the same name as another property but an incompatible return type",
                    DiagnosticLocationFactory.FromSymbol(property));
            }
        }

        return FindPropertyMethodCollision(methods, seen, ct);
    }

    private static UnsupportedMemberDiagnostic? FindPropertyMethodCollision(
        IEnumerable<IMethodSymbol> methods,
        Dictionary<string, IPropertySymbol> properties,
        CancellationToken ct)
    {
        foreach (var method in methods)
        {
            ct.ThrowIfCancellationRequested();

            if (properties.TryGetValue(method.Name, out var property))
            {
                return new UnsupportedMemberDiagnostic(
                    $"method '{method.Name}' has the same name as sub-service property '{property.Name}'; rename one member because generated proxies cannot expose both names",
                    DiagnosticLocationFactory.FromSymbol(method));
            }
        }

        return null;
    }
}
