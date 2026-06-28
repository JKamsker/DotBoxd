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

        return null;
    }
}
