using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxd.Services.SourceGenerator;

internal static class ServiceShapeValidator
{
    public static UnsupportedMemberDiagnostic? GetUnsupportedMemberDiagnostic(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();
            var diagnostic = GetUnsupportedMemberDiagnostic(member);
            if (diagnostic is not null)
            {
                return diagnostic;
            }
        }

        return null;
    }

    public static UnsupportedMemberDiagnostic? CollectMethods(
        INamedTypeSymbol interfaceSymbol,
        List<IMethodSymbol> methods,
        CancellationToken ct)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();
            var diagnostic = GetUnsupportedMemberDiagnostic(member);
            if (diagnostic is not null)
            {
                return diagnostic;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                methods.Add(method);
            }
        }

        return null;
    }

    private static UnsupportedMemberDiagnostic? GetUnsupportedMemberDiagnostic(ISymbol member)
    {
        if (member is IPropertySymbol property)
        {
            return CreateDiagnostic(
                property,
                $"interface property '{property.Name}' is not supported; DotBoxd services may declare methods only");
        }

        if (member is IEventSymbol eventSymbol)
        {
            return CreateDiagnostic(
                eventSymbol,
                $"interface event '{eventSymbol.Name}' is not supported; DotBoxd services may declare methods only");
        }

        if (member is IMethodSymbol method)
        {
            if (method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility != Accessibility.Public)
            {
                return CreateDiagnostic(
                    method,
                    $"non-public interface method '{method.Name}' is not supported; DotBoxd services may declare public instance methods only");
            }

            if (method.MethodKind == MethodKind.Ordinary && method.IsStatic)
            {
                return CreateDiagnostic(
                    method,
                    $"static interface method '{method.Name}' is not supported; DotBoxd services may declare instance methods only");
            }

            if (method.MethodKind is not MethodKind.Ordinary and not MethodKind.PropertyGet
                and not MethodKind.PropertySet and not MethodKind.EventAdd and not MethodKind.EventRemove)
            {
                return CreateDiagnostic(
                    method,
                    $"interface member '{method.Name}' has unsupported method kind '{method.MethodKind}'");
            }
        }

        return null;
    }

    private static IEnumerable<ISymbol> EnumerateInterfaceMembers(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var members = interfaceSymbol.GetMembers();
        ct.ThrowIfCancellationRequested();
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            yield return member;
        }

        ct.ThrowIfCancellationRequested();
        var baseInterfaces = interfaceSymbol.AllInterfaces;
        ct.ThrowIfCancellationRequested();
        foreach (var baseInterface in baseInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            var baseMembers = baseInterface.GetMembers();
            ct.ThrowIfCancellationRequested();
            foreach (var member in baseMembers)
            {
                ct.ThrowIfCancellationRequested();
                yield return member;
            }
        }
    }

    private static UnsupportedMemberDiagnostic CreateDiagnostic(ISymbol symbol, string reason) =>
        new(reason, DiagnosticLocationFactory.FromSymbol(symbol));
}
