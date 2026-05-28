using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class ServiceModelFactory
{
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";

    public static ServiceResult GetServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        try
        {
            return BuildServiceResult(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = context.TargetSymbol?.ToDisplayString() ?? "<unknown>";
            return new ServiceResult(
                Model: null,
                Error: new GeneratorError(name, ex.ToString()),
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
                ServiceLocation: default,
                QualifiedInterfaceName: string.Empty,
                ServiceDiagnostic: null);
        }
    }

    private static ServiceResult BuildServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
        {
            return default;
        }

        var displayName = interfaceSymbol.ToDisplayString();
        var serviceLocation = DiagnosticLocationFactory.FromSymbol(interfaceSymbol);
        var serviceNamespace = GetNamespace(interfaceSymbol.ContainingNamespace);
        var qualifiedInterfaceName = IdentifierHelpers.QualifyTypeName(
            serviceNamespace,
            interfaceSymbol.Name);

        if (interfaceSymbol.IsGenericType)
        {
            return RejectedService(
                displayName,
                "generic service interfaces are not supported; declare a non-generic interface and forward to a generic helper if needed",
                serviceLocation,
                qualifiedInterfaceName);
        }

        if (interfaceSymbol.ContainingType is not null)
        {
            return RejectedService(
                displayName,
                "nested service interfaces are not supported; declare the interface at namespace scope",
                serviceLocation,
                qualifiedInterfaceName);
        }

        if (interfaceSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return RejectedService(
                displayName,
                "service interfaces must be public because generated proxy, dispatcher, and extension APIs are public",
                serviceLocation,
                qualifiedInterfaceName);
        }

        var unsupportedMemberDiagnostic = ServiceShapeValidator.GetUnsupportedMemberDiagnostic(interfaceSymbol, ct);
        if (unsupportedMemberDiagnostic is not null)
        {
            return RejectedService(
                displayName,
                unsupportedMemberDiagnostic.Value.Reason,
                unsupportedMemberDiagnostic.Value.Location,
                qualifiedInterfaceName);
        }

        ct.ThrowIfCancellationRequested();

        var serviceName = GetConfiguredServiceName(context) ?? interfaceSymbol.Name;
        var cancellationTokenSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(CancellationTokenFullName);
        var methods = new List<MethodModel>();
        var methodLocations = new List<DiagnosticLocation>();
        var methodDiagnostics = new List<MethodDiagnostic>();
        var seenSignatures = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);

        foreach (var methodSymbol in EnumerateMethods(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();

            var sigKey = GetSignatureKey(methodSymbol, ct);
            if (seenSignatures.TryGetValue(sigKey, out var existingMethod))
            {
                if (!HasSameReturnShape(existingMethod, methodSymbol))
                {
                    return RejectedService(
                        displayName,
                        $"inherited method '{methodSymbol.Name}' has the same signature as another method but an incompatible return type",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                continue;
            }
            seenSignatures[sigKey] = methodSymbol;

            var method = MethodModelFactory.Build(
                displayName,
                methodSymbol,
                cancellationTokenSymbol,
                methodDiagnostics,
                ct,
                out var methodLocation);

            methods.Add(method);
            methodLocations.Add(methodLocation);
        }

        WireNameValidator.MarkDuplicateWireNames(displayName, methods, methodLocations, methodDiagnostics, ct);

        return new ServiceResult(
            Model: new ServiceModel(
                Namespace: serviceNamespace,
                InterfaceName: interfaceSymbol.Name,
                ServiceName: LiteralHelpers.EscapeStringLiteral(serviceName),
                Methods: methods.ToEquatableArray()),
            Error: null,
            MethodDiagnostics: methodDiagnostics.ToEquatableArray(),
            MethodLocations: methodLocations.ToEquatableArray(),
            ServiceLocation: serviceLocation,
            QualifiedInterfaceName: qualifiedInterfaceName,
            ServiceDiagnostic: null);
    }

    private static ServiceResult RejectedService(
        string displayName,
        string reason,
        DiagnosticLocation location,
        string qualifiedInterfaceName) =>
        new(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: location,
            QualifiedInterfaceName: qualifiedInterfaceName,
            ServiceDiagnostic: new ServiceDiagnostic(displayName, reason, location));

    private static string? GetConfiguredServiceName(GeneratorAttributeSyntaxContext context)
    {
        foreach (var attr in context.Attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    return s;
                }
            }
        }

        return null;
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

    private static IEnumerable<IMethodSymbol> EnumerateMethods(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var members = interfaceSymbol.GetMembers();
        ct.ThrowIfCancellationRequested();
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
            {
                yield return m;
            }
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
                if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                {
                    yield return m;
                }
            }
        }
    }

    private static bool HasSameReturnShape(IMethodSymbol left, IMethodSymbol right) =>
        left.RefKind == right.RefKind &&
        SymbolEqualityComparer.Default.Equals(left.ReturnType, right.ReturnType);

    private static string GetSignatureKey(IMethodSymbol methodSymbol, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var parameter in methodSymbol.Parameters)
        {
            ct.ThrowIfCancellationRequested();
            parts.Add(parameter.RefKind + ":" + parameter.Type.ToDisplayString());
        }

        return methodSymbol.Name + "`" + methodSymbol.Arity + "(" + string.Join(",", parts) + ")";
    }
}
