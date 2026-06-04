using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class MethodModelFactory
{
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";

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
        var typeParameterList = MethodSignatureFormatter.GetTypeParameterList(methodSymbol, ct);
        var constraintClauses = MethodSignatureFormatter.GetConstraintClauses(methodSymbol, ct);
        string? unsupportedReason = null;
        methodLocation = DiagnosticLocationFactory.FromSymbol(methodSymbol);
        var unsupportedLocation = methodLocation;
        var requiresUnsafeSignature = RpcTypeValidator.RequiresUnsafeContext(returnType, ct);

        // An explicit empty/whitespace [ShaRpcMethod(Name = "")] compiles but throws ArgumentException on
        // the first call (the empty wire name fails validation), so reject it at build time.
        var configuredMethodName = GetConfiguredMethodName(methodSymbol);
        if (configuredMethodName is not null && string.IsNullOrWhiteSpace(configuredMethodName))
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                "[ShaRpcMethod(Name = ...)] wire name must not be empty or whitespace",
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
            RpcTypeValidator.GetUnsupportedTypeReason(returnType, "return type", ct),
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
                RpcTypeValidator.GetUnsupportedTypeReason(param.Type, $"parameter '{param.Name}'", ct),
                parameterLocation);
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                RpcTypeValidator.GetUnsupportedSubServicePayloadReason(
                    param.Type,
                    $"parameter '{param.Name}'",
                    ct,
                    validationCache),
                parameterLocation);

            // A cancellation-token default is always emitted as "= default"; capture the literal text of
            // any other parameter's explicit default so the generated proxy/async-sibling preserve it.
            var defaultValueLiteral = isCancellationToken ? string.Empty : FormatDefaultValueLiteral(param) ?? string.Empty;

            parameters.Add(new ParameterModel(
                IdentifierHelpers.EscapeIdentifier(param.Name),
                param.Type.ToDisplayString(s_qualifiedFormat),
                MethodSignatureFacts.GetCanonicalType(param.Type, methodSymbol, ct),
                ParameterRefKindKeyword(param.RefKind),
                isCancellationToken,
                param.HasExplicitDefaultValue,
                defaultValueLiteral));
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
            RawRpcName: configuredRpcName);
    }

    internal static string GetExplicitImplementationType(INamedTypeSymbol type) =>
        type.ToDisplayString(s_qualifiedFormat);

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

    private static string ParameterRefKindKeyword(RefKind kind) =>
        kind.ToString() switch
        {
            "Ref" => "ref ",
            "In" => "in ",
            "Out" => "out ",
            "RefReadOnly" => "ref readonly ",
            "RefReadOnlyParameter" => "ref readonly ",
            _ => string.Empty,
        };

    private static string ReturnRefKindKeyword(RefKind kind) =>
        kind.ToString() switch
        {
            "Ref" => "ref ",
            "In" => "ref readonly ",
            "RefReadOnly" => "ref readonly ",
            "RefReadOnlyParameter" => "ref readonly ",
            _ => string.Empty,
        };

    private static string RefKindDisplay(RefKind kind, bool isReturn)
    {
        var text = kind.ToString();
        return text switch
        {
            "In" when isReturn => "ref readonly",
            "RefReadOnly" => "ref readonly",
            "RefReadOnlyParameter" => "ref readonly",
            _ => text.ToLowerInvariant(),
        };
    }

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

    /// <summary>
    /// Formats a non-cancellation-token parameter's explicit default value as the C# literal to emit
    /// in a generated signature, or <see langword="null"/> when it cannot be safely expressed — in
    /// which case the caller emits no default rather than a wrong one (preserving prior behaviour).
    /// </summary>
    private static string? FormatDefaultValueLiteral(IParameterSymbol param)
    {
        if (!param.HasExplicitDefaultValue)
        {
            return null;
        }

        var value = param.ExplicitDefaultValue;
        var type = param.Type;

        if (value is null)
        {
            // "= null" for reference / nullable value types; "= default" for a non-nullable value
            // type's default (always valid and produces the same value).
            return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        }

        // Enum default: cast the (boxed underlying) constant to the fully-qualified enum type so the
        // literal is unambiguous regardless of the generated file's usings. Unwrap Nullable<TEnum>.
        var underlyingType = type;
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable)
        {
            underlyingType = nullable.TypeArguments[0];
        }

        if (underlyingType.TypeKind == TypeKind.Enum)
        {
            var underlyingLiteral = FormatPrimitiveLiteral(value);
            return underlyingLiteral is null
                ? null
                : "(" + underlyingType.ToDisplayString(s_qualifiedFormat) + ")" + underlyingLiteral;
        }

        return FormatPrimitiveLiteral(value);
    }

    private static string? FormatPrimitiveLiteral(object value) => value switch
    {
        bool b => b ? "true" : "false",
        string s => "\"" + LiteralHelpers.EscapeStringLiteral(s) + "\"",
        char c => "'" + EscapeCharLiteral(c) + "'",
        sbyte v => v.ToString(CultureInfo.InvariantCulture),
        byte v => v.ToString(CultureInfo.InvariantCulture),
        short v => v.ToString(CultureInfo.InvariantCulture),
        ushort v => v.ToString(CultureInfo.InvariantCulture),
        int v => v.ToString(CultureInfo.InvariantCulture),
        uint v => v.ToString(CultureInfo.InvariantCulture) + "U",
        long v => v.ToString(CultureInfo.InvariantCulture) + "L",
        ulong v => v.ToString(CultureInfo.InvariantCulture) + "UL",
        // NaN/Infinity have no literal form; fall back to "no default" rather than emit invalid code.
        float v => float.IsNaN(v) || float.IsInfinity(v) ? null : v.ToString("R", CultureInfo.InvariantCulture) + "F",
        double v => double.IsNaN(v) || double.IsInfinity(v) ? null : v.ToString("R", CultureInfo.InvariantCulture) + "D",
        decimal v => v.ToString(CultureInfo.InvariantCulture) + "M",
        _ => null,
    };

    private static string EscapeCharLiteral(char c) => c switch
    {
        '\'' => "\\'",
        '\\' => "\\\\",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        // U+2028 (LINE SEPARATOR) and U+2029 (PARAGRAPH SEPARATOR) are line terminators inside a char
        // literal (CS1010) but are NOT control chars, so route them through the same \uXXXX escape path.
        // Mirrors LiteralHelpers.EscapeStringLiteral, which escapes both code points explicitly.
        _ => char.IsControl(c) || c == 0x2028 || c == 0x2029
            ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)
            : c.ToString(),
    };
}
