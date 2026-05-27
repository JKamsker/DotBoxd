using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class ServiceModelFactory
{
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";

    private static readonly SymbolDisplayFormat s_qualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

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
        var serviceLocation = GetSymbolLocation(interfaceSymbol);

        if (interfaceSymbol.IsGenericType)
        {
            return RejectedService(
                displayName,
                "generic service interfaces are not supported; declare a non-generic interface and forward to a generic helper if needed",
                serviceLocation);
        }

        if (interfaceSymbol.ContainingType is not null)
        {
            return RejectedService(
                displayName,
                "nested service interfaces are not supported; declare the interface at namespace scope",
                serviceLocation);
        }

        if (interfaceSymbol.DeclaredAccessibility != Accessibility.Public)
        {
            return RejectedService(
                displayName,
                "service interfaces must be public because generated proxy, dispatcher, and extension APIs are public",
                serviceLocation);
        }

        var unsupportedMemberReason = ServiceShapeValidator.GetUnsupportedMemberReason(interfaceSymbol);
        if (unsupportedMemberReason is not null)
        {
            return RejectedService(displayName, unsupportedMemberReason, serviceLocation);
        }

        ct.ThrowIfCancellationRequested();

        var serviceName = GetConfiguredServiceName(context) ?? interfaceSymbol.Name;
        var cancellationTokenSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(CancellationTokenFullName);
        var methods = new List<MethodModel>();
        var methodLocations = new List<DiagnosticLocation>();
        var methodDiagnostics = new List<MethodDiagnostic>();
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var methodSymbol in EnumerateMethods(interfaceSymbol))
        {
            ct.ThrowIfCancellationRequested();

            var sigKey = methodSymbol.Name + "`" + methodSymbol.Arity + "(" +
                string.Join(",", methodSymbol.Parameters.Select(p => p.RefKind + ":" + p.Type.ToDisplayString())) + ")";
            if (!seenSignatures.Add(sigKey))
            {
                continue;
            }

            var method = BuildMethodModel(
                displayName,
                methodSymbol,
                cancellationTokenSymbol,
                methodDiagnostics,
                out var methodLocation);

            methods.Add(method);
            methodLocations.Add(methodLocation);
        }

        WireNameValidator.MarkDuplicateWireNames(displayName, methods, methodLocations, methodDiagnostics);

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();

        return new ServiceResult(
            Model: new ServiceModel(
                Namespace: ns,
                InterfaceName: interfaceSymbol.Name,
                ServiceName: LiteralHelpers.EscapeStringLiteral(serviceName),
                Methods: methods.ToEquatableArray()),
            Error: null,
            MethodDiagnostics: methodDiagnostics.ToEquatableArray(),
            ServiceDiagnostic: null);
    }

    private static ServiceResult RejectedService(
        string displayName,
        string reason,
        DiagnosticLocation location) =>
        new(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
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

    private static IEnumerable<IMethodSymbol> EnumerateMethods(INamedTypeSymbol interfaceSymbol)
    {
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
            {
                yield return m;
            }
        }

        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            foreach (var member in baseInterface.GetMembers())
            {
                if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                {
                    yield return m;
                }
            }
        }
    }

    private static MethodModel BuildMethodModel(
        string displayName,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? cancellationTokenSymbol,
        List<MethodDiagnostic> methodDiagnostics,
        out DiagnosticLocation methodLocation)
    {
        var returnType = methodSymbol.ReturnType;
        var returnKind = ReturnTypeClassifier.Classify(returnType, out var unwrappedReturnType, out var subService);
        var typeParameterList = MethodSignatureFormatter.GetTypeParameterList(methodSymbol);
        var constraintClauses = MethodSignatureFormatter.GetConstraintClauses(methodSymbol);
        string? unsupportedReason = ReturnTypeClassifier.GetUnsupportedServiceReturnReason(returnType);
        methodLocation = GetSymbolLocation(methodSymbol);

        var parameters = new List<ParameterModel>();
        var hasCancellationToken = false;
        var cancellationTokenCount = 0;
        if (methodSymbol.IsGenericMethod)
        {
            unsupportedReason ??= "generic service methods are not supported; expose a non-generic RPC method instead";
        }

        foreach (var param in methodSymbol.Parameters)
        {
            var isCancellationToken = cancellationTokenSymbol is not null &&
                SymbolEqualityComparer.Default.Equals(param.Type, cancellationTokenSymbol);

            if (isCancellationToken)
            {
                cancellationTokenCount++;
                hasCancellationToken = true;
            }

            if (param.RefKind != RefKind.None)
            {
                unsupportedReason ??=
                    $"parameter '{param.Name}' uses an unsupported pass-by-reference kind '{param.RefKind.ToString().ToLowerInvariant()}'";
            }

            parameters.Add(new ParameterModel(
                EscapeIdentifier(param.Name),
                param.Type.ToDisplayString(s_qualifiedFormat),
                RefKindKeyword(param.RefKind),
                isCancellationToken,
                param.HasExplicitDefaultValue));
        }

        if (cancellationTokenCount > 1)
        {
            unsupportedReason ??= "multiple CancellationToken parameters are not supported";
        }

        if (unsupportedReason is not null)
        {
            methodDiagnostics.Add(new MethodDiagnostic(
                displayName,
                methodSymbol.Name,
                unsupportedReason,
                methodLocation));
        }

        return new MethodModel(
            Name: EscapeIdentifier(methodSymbol.Name),
            RpcName: LiteralHelpers.EscapeStringLiteral(GetConfiguredMethodName(methodSymbol) ?? methodSymbol.Name),
            ReturnKind: returnKind,
            UnwrappedReturnType: unwrappedReturnType,
            HasCancellationToken: hasCancellationToken,
            Parameters: parameters.ToEquatableArray(),
            TypeParameterList: typeParameterList,
            ConstraintClauses: constraintClauses,
            UnsupportedReason: unsupportedReason,
            SubService: subService);
    }

    private static string? GetConfiguredMethodName(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != ShaRpcMethodAttributeName)
            {
                continue;
            }

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

    private static string RefKindKeyword(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.In => "in ",
        RefKind.Out => "out ",
        _ => string.Empty,
    };

    private static DiagnosticLocation GetSymbolLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return DiagnosticLocation.FromLocation(location);
            }
        }

        return default;
    }

    private static string EscapeIdentifier(string name)
    {
        var kind = Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name);
        return kind == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None ? name : "@" + name;
    }
}
