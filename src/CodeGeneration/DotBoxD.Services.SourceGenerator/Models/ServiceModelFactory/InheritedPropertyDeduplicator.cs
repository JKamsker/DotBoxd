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
        string proxyName,
        List<IPropertySymbol> uniqueProperties,
        CancellationToken ct)
    {
        var seen = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            ct.ThrowIfCancellationRequested();

            var generatedCollision = GetGeneratedMemberCollision(property, proxyName);
            if (generatedCollision is not null)
            {
                return generatedCollision;
            }

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

    private static UnsupportedMemberDiagnostic? GetGeneratedMemberCollision(
        IPropertySymbol property,
        string proxyName)
    {
        var name = property.Name;
        if (name != proxyName &&
            name != "_invoker" &&
            name != "_instanceId" &&
            name != "Equals" &&
            name != "GetHashCode" &&
            name != "GetType" &&
            name != "ToString")
        {
            return null;
        }

        return new UnsupportedMemberDiagnostic(
            $"property '{name}' collides with generated proxy member '{name}'; rename the property because generated proxies cannot expose that member name",
            DiagnosticLocationFactory.FromSymbol(property));
    }
}
