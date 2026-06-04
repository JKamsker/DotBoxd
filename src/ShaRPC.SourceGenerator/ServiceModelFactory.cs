using System;
using System.Collections.Generic;
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
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            // An explicit empty/whitespace [ShaRpcService(Name = "")] compiles but no inbound call can ever
            // match the empty wire name, so every dispatch fails at runtime. Reject it at build time.
            return RejectedService(
                displayName,
                "[ShaRpcService(Name = ...)] wire name must not be empty or whitespace",
                serviceLocation,
                qualifiedInterfaceName);
        }

        var cancellationTokenSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(CancellationTokenFullName);
        var methods = new List<MethodModel>();
        var methodLocations = new List<DiagnosticLocation>();
        var methodDiagnostics = new List<MethodDiagnostic>();
        var seenSignatures = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        var seenSignatureIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var validationCache = SharedRpcTypeValidationCache.Get(context.SemanticModel.Compilation);

        foreach (var methodSymbol in EnumerateMethods(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();

            var sigKey = MethodSignatureFacts.GetSignatureKey(methodSymbol, ct);
            if (seenSignatures.TryGetValue(sigKey, out var existingMethod))
            {
                if (!InheritedMethodDeduplicator.HasCompatibleReturnShape(existingMethod, methodSymbol, ct))
                {
                    return RejectedService(
                        displayName,
                        $"inherited method '{methodSymbol.Name}' has the same signature as another method but an incompatible return type",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                if (!InheritedMethodDeduplicator.HasSameParameterRefKinds(existingMethod, methodSymbol))
                {
                    return RejectedService(
                        displayName,
                        $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible parameter ref kinds",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                if (!MethodSignatureFacts.HaveSameGenericConstraints(existingMethod, methodSymbol, ct))
                {
                    return RejectedService(
                        displayName,
                        $"inherited generic method '{methodSymbol.Name}' has the same signature as another method but incompatible generic constraints",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                if (!InheritedMethodDeduplicator.HasSameNullableAnnotations(existingMethod, methodSymbol, ct))
                {
                    return RejectedService(
                        displayName,
                        $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible nullable annotations",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                if (!TupleElementNameComparer.HasSameElementNames(existingMethod, methodSymbol, ct))
                {
                    return RejectedService(
                        displayName,
                        $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible tuple element names",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                if (!InheritedMethodDeduplicator.HasSameEffectiveWireName(existingMethod, methodSymbol))
                {
                    return RejectedService(
                        displayName,
                        $"inherited method '{methodSymbol.Name}' has the same signature as another method but a different wire method name",
                        DiagnosticLocationFactory.FromSymbol(methodSymbol),
                        qualifiedInterfaceName);
                }

                var existingIndex = seenSignatureIndexes[sigKey];
                methods[existingIndex] = InheritedMethodDeduplicator.AddAdditionalExplicitImplementation(
                    methods[existingIndex],
                    methodSymbol.ContainingType);
                continue;
            }
            seenSignatures[sigKey] = methodSymbol;
            seenSignatureIndexes[sigKey] = methods.Count;

            var method = MethodModelFactory.Build(
                displayName,
                methodSymbol,
                cancellationTokenSymbol,
                validationCache,
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
                Methods: methods.ToEquatableArray(),
                RawServiceName: serviceName),
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

}
