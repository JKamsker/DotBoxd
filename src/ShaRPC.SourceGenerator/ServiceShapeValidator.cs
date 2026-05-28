using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class ServiceShapeValidator
{
    public static UnsupportedMemberDiagnostic? GetUnsupportedMemberDiagnostic(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (member is IPropertySymbol property)
            {
                return CreateDiagnostic(
                    property,
                    $"interface property '{property.Name}' is not supported; ShaRPC services may declare methods only");
            }

            if (member is IEventSymbol eventSymbol)
            {
                return CreateDiagnostic(
                    eventSymbol,
                    $"interface event '{eventSymbol.Name}' is not supported; ShaRPC services may declare methods only");
            }

            if (member is IMethodSymbol method)
            {
                if (method.MethodKind == MethodKind.Ordinary &&
                    method.DeclaredAccessibility != Accessibility.Public)
                {
                    return CreateDiagnostic(
                        method,
                        $"non-public interface method '{method.Name}' is not supported; ShaRPC services may declare public instance methods only");
                }

                if (method.MethodKind == MethodKind.Ordinary && method.IsStatic)
                {
                    return CreateDiagnostic(
                        method,
                        $"static interface method '{method.Name}' is not supported; ShaRPC services may declare instance methods only");
                }

                if (method.MethodKind is not MethodKind.Ordinary and not MethodKind.PropertyGet
                    and not MethodKind.PropertySet and not MethodKind.EventAdd and not MethodKind.EventRemove)
                {
                    return CreateDiagnostic(
                        method,
                        $"interface member '{method.Name}' has unsupported method kind '{method.MethodKind}'");
                }
            }
        }

        return null;
    }

    private static IEnumerable<ISymbol> EnumerateInterfaceMembers(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            yield return member;
        }

        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var member in baseInterface.GetMembers())
            {
                ct.ThrowIfCancellationRequested();
                yield return member;
            }
        }
    }

    private static UnsupportedMemberDiagnostic CreateDiagnostic(ISymbol symbol, string reason) =>
        new(reason, DiagnosticLocationFactory.FromSymbol(symbol));
}
