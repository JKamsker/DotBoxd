using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class ServiceShapeValidator
{
    public static string? GetUnsupportedMemberReason(INamedTypeSymbol interfaceSymbol)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol))
        {
            if (member is IPropertySymbol property)
            {
                return $"interface property '{property.Name}' is not supported; ShaRPC services may declare methods only";
            }

            if (member is IEventSymbol eventSymbol)
            {
                return $"interface event '{eventSymbol.Name}' is not supported; ShaRPC services may declare methods only";
            }

            if (member is IMethodSymbol method)
            {
                if (method.MethodKind == MethodKind.Ordinary && method.IsStatic)
                {
                    return $"static interface method '{method.Name}' is not supported; ShaRPC services may declare instance methods only";
                }

                if (method.MethodKind is not MethodKind.Ordinary and not MethodKind.PropertyGet
                    and not MethodKind.PropertySet and not MethodKind.EventAdd and not MethodKind.EventRemove)
                {
                    return $"interface member '{method.Name}' has unsupported method kind '{method.MethodKind}'";
                }
            }
        }

        return null;
    }

    private static IEnumerable<ISymbol> EnumerateInterfaceMembers(INamedTypeSymbol interfaceSymbol)
    {
        foreach (var member in interfaceSymbol.GetMembers())
        {
            yield return member;
        }

        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            foreach (var member in baseInterface.GetMembers())
            {
                yield return member;
            }
        }
    }
}
