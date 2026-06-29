using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Generation;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private const string DotBoxDMethodAttributeName = ServicesGeneratorTypeNames.DotBoxDMethodAttribute;

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static MethodModel Build(
        string displayName,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? cancellationTokenSymbol,
        RpcTypeValidationCache validationCache,
        List<MethodDiagnostic> methodDiagnostics,
        CancellationToken ct,
        out DiagnosticLocation methodLocation)
    {
        ct.ThrowIfCancellationRequested();

        var returnType = methodSymbol.ReturnType;
        var returnKind = ReturnTypeClassifier.Classify(returnType, ct, out var unwrappedReturnType, out var subService);
        var metadataTypes = MethodMetadataTypesFactory.Get(methodSymbol, returnKind, ct);
        var declaredReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        var typeParameterList = MethodSignatureFormatter.GetTypeParameterList(methodSymbol, ct);
        var constraintClauses = MethodSignatureFormatter.GetConstraintClauses(methodSymbol, ct);
        string? unsupportedReason = null;
        methodLocation = DiagnosticLocationFactory.FromSymbol(methodSymbol);
        var unsupportedLocation = methodLocation;
        var requiresUnsafeSignature = RpcTypeValidator.RequiresUnsafeContext(returnType, ct);

        // An explicit empty/whitespace [DotBoxDMethod(Name = "")] compiles but throws ArgumentException on
        // the first call (the empty wire name fails validation), so reject it at build time.
        var configuredMethodName = GetConfiguredMethodName(methodSymbol);
        if (configuredMethodName is not null && string.IsNullOrWhiteSpace(configuredMethodName))
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                "[DotBoxDMethod(Name = ...)] wire name must not be empty or whitespace",
                methodLocation);
        }

        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            ReturnTypeClassifier.GetUnsupportedServiceReturnReason(returnType, ct),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            RpcTypeValidator.GetUnsupportedTypeReason(
                returnType,
                "return type",
                ct,
                allowTopLevelAsyncWrapper: true),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            RpcTypeValidator.GetUnsupportedSubServicePayloadReason(
                returnType,
                returnKind,
                "return type",
                ct,
                validationCache),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            GetUnsupportedNullableStreamingReturnReason(returnType, returnKind),
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
                $"return value uses an unsupported pass-by-reference kind '{RefKindDisplay(methodSymbol.RefKind, isReturn: true)}'",
                methodLocation);
        }

        foreach (var param in methodSymbol.Parameters)
        {
            ct.ThrowIfCancellationRequested();

            var parameterLocation = DiagnosticLocationFactory.FromSymbol(param);
            requiresUnsafeSignature |= RpcTypeValidator.RequiresUnsafeContext(param.Type, ct);
            var isCancellationToken = cancellationTokenSymbol is not null &&
                SymbolEqualityComparer.Default.Equals(param.Type, cancellationTokenSymbol);
            var (streamKind, streamItemType) = ClassifyParameterStream(param.Type, ct);

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
                    $"parameter '{param.Name}' uses an unsupported pass-by-reference kind '{RefKindDisplay(param.RefKind, isReturn: false)}'",
                    parameterLocation);
            }

            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                GetUnsupportedParameterTypeReason(param.Type, streamKind, streamItemType, param.Name, ct),
                parameterLocation);
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                GetUnsupportedParameterSubServiceReason(
                    param.Type,
                    streamKind,
                    streamItemType,
                    param.Name,
                    ct,
                    validationCache),
                parameterLocation);
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                GetUnsupportedNullableStreamingParameterReason(param.Type, streamKind, param.Name),
                parameterLocation);

            // A cancellation-token default is always emitted as "= default"; capture the literal text of
            // any other parameter's explicit default so the generated proxy/async-sibling preserve it.
            var defaultValueLiteral = isCancellationToken ? string.Empty : FormatDefaultValueLiteral(param) ?? string.Empty;

            parameters.Add(new ParameterModel(
                IdentifierHelpers.EscapeIdentifier(param.Name),
                param.Type.ToDisplayString(s_qualifiedFormat),
                MethodSignatureFacts.GetCanonicalType(param.Type, methodSymbol, ct),
                ParameterRefKindKeyword(param.RefKind),
                param.IsParams,
                isCancellationToken,
                param.HasExplicitDefaultValue,
                defaultValueLiteral,
                streamKind,
                streamItemType?.ToDisplayString(s_qualifiedFormat),
                MetadataType: TypeOfExpressionFormatter.Format(param.Type, ct),
                CallerInfoAttributePrefix: BuildCallerInfoAttributePrefix(param, ct)));
        }

        if (unsupportedReason is not null)
        {
            methodDiagnostics.Add(new MethodDiagnostic(
                displayName,
                methodSymbol.Name,
                unsupportedReason,
                unsupportedLocation));
        }

        var configuredRpcName = configuredMethodName ?? methodSymbol.Name;

        return new MethodModel(
            Name: IdentifierHelpers.EscapeIdentifier(methodSymbol.Name),
            ExplicitImplementationType: GetExplicitImplementationType(methodSymbol.ContainingType),
            RpcName: LiteralHelpers.EscapeStringLiteral(configuredRpcName),
            ReturnKind: returnKind,
            DeclaredReturnType: declaredReturnType,
            UnwrappedReturnType: unwrappedReturnType,
            ReturnRefKindKeyword: ReturnRefKindKeyword(methodSymbol.RefKind),
            HasCancellationToken: hasCancellationToken,
            Parameters: parameters.ToEquatableArray(),
            AdditionalExplicitImplementationTypes: EquatableArray<string>.Empty,
            RequiresUnsafeSignature: requiresUnsafeSignature,
            TypeParameterCount: methodSymbol.Arity,
            TypeParameterList: typeParameterList,
            ConstraintClauses: constraintClauses,
            UnsupportedReason: unsupportedReason,
            SubService: subService,
            RawRpcName: configuredRpcName,
            MetadataReturnType: metadataTypes.ReturnType,
            MetadataResultType: metadataTypes.ResultType);
    }

    internal static string GetExplicitImplementationType(INamedTypeSymbol type) =>
        type.ToDisplayString(s_qualifiedFormat);

    private static string? GetConfiguredMethodName(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != DotBoxDMethodAttributeName)
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

    private static string BuildCallerInfoAttributePrefix(
        IParameterSymbol parameter,
        CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in parameter.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            switch (attr.AttributeClass?.ToDisplayString())
            {
                case "System.Runtime.CompilerServices.CallerMemberNameAttribute":
                    attributes.Append("[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.CallerFilePathAttribute":
                    attributes.Append("[global::System.Runtime.CompilerServices.CallerFilePathAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.CallerLineNumberAttribute":
                    attributes.Append("[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute":
                    AppendCallerArgumentExpressionAttribute(attributes, attr);
                    break;
            }
        }

        return attributes.ToString();
    }

    private static void AppendCallerArgumentExpressionAttribute(StringBuilder sb, AttributeData attr)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string parameterName)
        {
            return;
        }

        sb.Append("[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(parameterName))
            .Append("\")] ");
    }

}
