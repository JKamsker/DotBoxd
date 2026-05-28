using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class MethodModelFactory
{
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static MethodModel Build(
        string displayName,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? cancellationTokenSymbol,
        List<MethodDiagnostic> methodDiagnostics,
        CancellationToken ct,
        out DiagnosticLocation methodLocation)
    {
        ct.ThrowIfCancellationRequested();

        var returnType = methodSymbol.ReturnType;
        var returnKind = ReturnTypeClassifier.Classify(returnType, ct, out var unwrappedReturnType, out var subService);
        var typeParameterList = MethodSignatureFormatter.GetTypeParameterList(methodSymbol);
        var constraintClauses = MethodSignatureFormatter.GetConstraintClauses(methodSymbol);
        string? unsupportedReason = null;
        methodLocation = DiagnosticLocationFactory.FromSymbol(methodSymbol);
        var unsupportedLocation = methodLocation;
        var requiresUnsafeSignature = RpcTypeValidator.RequiresUnsafeContext(returnType, ct);

        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            ReturnTypeClassifier.GetUnsupportedServiceReturnReason(returnType, ct),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            RpcTypeValidator.GetUnsupportedTypeReason(returnType, "return type", ct),
            methodLocation);

        var parameters = new List<ParameterModel>();
        var hasCancellationToken = false;
        var cancellationTokenCount = 0;
        if (methodSymbol.IsGenericMethod)
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                "generic service methods are not supported; expose a non-generic RPC method instead",
                methodLocation);
        }

        if (methodSymbol.RefKind != RefKind.None)
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                $"return value uses an unsupported pass-by-reference kind '{methodSymbol.RefKind.ToString().ToLowerInvariant()}'",
                methodLocation);
        }

        foreach (var param in methodSymbol.Parameters)
        {
            ct.ThrowIfCancellationRequested();

            var parameterLocation = DiagnosticLocationFactory.FromSymbol(param);
            requiresUnsafeSignature |= RpcTypeValidator.RequiresUnsafeContext(param.Type, ct);
            var isCancellationToken = cancellationTokenSymbol is not null &&
                SymbolEqualityComparer.Default.Equals(param.Type, cancellationTokenSymbol);

            if (isCancellationToken)
            {
                cancellationTokenCount++;
                hasCancellationToken = true;
                if (cancellationTokenCount > 1)
                {
                    SetUnsupported(
                        ref unsupportedReason,
                        ref unsupportedLocation,
                        "multiple CancellationToken parameters are not supported",
                        parameterLocation);
                }
            }

            if (param.RefKind != RefKind.None)
            {
                SetUnsupported(
                    ref unsupportedReason,
                    ref unsupportedLocation,
                    $"parameter '{param.Name}' uses an unsupported pass-by-reference kind '{param.RefKind.ToString().ToLowerInvariant()}'",
                    parameterLocation);
            }

            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                RpcTypeValidator.GetUnsupportedTypeReason(param.Type, $"parameter '{param.Name}'", ct),
                parameterLocation);

            parameters.Add(new ParameterModel(
                IdentifierHelpers.EscapeIdentifier(param.Name),
                param.Type.ToDisplayString(s_qualifiedFormat),
                RefKindKeyword(param.RefKind),
                isCancellationToken,
                param.HasExplicitDefaultValue));
        }

        if (unsupportedReason is not null)
        {
            methodDiagnostics.Add(new MethodDiagnostic(
                displayName,
                methodSymbol.Name,
                unsupportedReason,
                unsupportedLocation));
        }

        return new MethodModel(
            Name: IdentifierHelpers.EscapeIdentifier(methodSymbol.Name),
            RpcName: LiteralHelpers.EscapeStringLiteral(GetConfiguredMethodName(methodSymbol) ?? methodSymbol.Name),
            ReturnKind: returnKind,
            UnwrappedReturnType: unwrappedReturnType,
            ReturnRefKindKeyword: RefKindKeyword(methodSymbol.RefKind),
            HasCancellationToken: hasCancellationToken,
            Parameters: parameters.ToEquatableArray(),
            RequiresUnsafeSignature: requiresUnsafeSignature,
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

    private static void SetUnsupported(
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation,
        string? reason,
        DiagnosticLocation location)
    {
        if (unsupportedReason is not null || reason is null)
        {
            return;
        }

        unsupportedReason = reason;
        unsupportedLocation = location;
    }
}
