using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelClientParameterSource
{
    public static string ParameterList(IMethodSymbol method)
    {
        var parts = new List<string>();
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var preserveMetadataDefaultAttributes =
                ShouldPreserveMetadataDefaultAttributes(method, i, out var defaultLiteral);
            parts.Add(Declaration(
                method.Parameters[i],
                isLast: i == method.Parameters.Length - 1,
                preserveMetadataDefaultAttributes,
                defaultLiteral));
        }

        return string.Join(", ", parts);
    }

    public static string ArgumentList(IMethodSymbol method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            parts.Add(Identifier(parameter.Name));
        }

        return string.Join(", ", parts);
    }

    public static string Declaration(IParameterSymbol parameter, bool isLast = false)
    {
        var preserveMetadataDefaultAttributes = ShouldPreserveMetadataDefaultAttributes(
            parameter,
            preserveOptionalAttributeDefault: false,
            out var defaultLiteral);
        return Declaration(parameter, isLast, preserveMetadataDefaultAttributes, defaultLiteral);
    }

    public static string Identifier(string name) => "@" + name;

    private static string ParamsModifier(IParameterSymbol parameter, bool isLast)
        => parameter.IsParams && isLast ? "params " : string.Empty;

    private static string Declaration(
        IParameterSymbol parameter,
        bool isLast,
        bool preserveMetadataDefaultAttributes,
        string? defaultLiteral)
        => AttributePrefix(parameter, preserveMetadataDefaultAttributes) +
           ParamsModifier(parameter, isLast) +
           TypeName(parameter.Type) +
           " " +
           Identifier(parameter.Name) +
           DefaultClause(preserveMetadataDefaultAttributes, defaultLiteral);

    private static bool ShouldPreserveMetadataDefaultAttributes(
        IMethodSymbol method,
        int parameterIndex,
        out string? defaultLiteral)
        => ShouldPreserveMetadataDefaultAttributes(
            method.Parameters[parameterIndex],
            ParameterDefaultValueEmitter.ShouldPreserveOptionalAttributeDefault(method, parameterIndex),
            out defaultLiteral);

    private static bool ShouldPreserveMetadataDefaultAttributes(
        IParameterSymbol parameter,
        bool preserveOptionalAttributeDefault,
        out string? defaultLiteral)
    {
        var hasDefaultValue = ParameterDefaultValueEmitter.HasDefaultValue(parameter);
        defaultLiteral = preserveOptionalAttributeDefault
            ? null
            : ParameterDefaultValueEmitter.FormatSignatureDefaultLiteral(
                parameter,
                hasDefaultValue,
                DefaultLiteralOptions.Analyzer);
        return preserveOptionalAttributeDefault ||
            ParameterDefaultValueEmitter.HasDateTimeConstantAttribute(parameter) ||
            (defaultLiteral is null && HasMetadataDefaultAttribute(parameter));
    }

    private static string DefaultClause(bool preserveMetadataDefaultAttributes, string? defaultLiteral)
        => preserveMetadataDefaultAttributes || defaultLiteral is null ? string.Empty : " = " + defaultLiteral;

    private static string AttributePrefix(IParameterSymbol parameter, bool preserveMetadataDefaultAttributes)
        => preserveMetadataDefaultAttributes
            ? ParameterDefaultValueEmitter.FormatMetadataDefaultAttributePrefix(
                parameter,
                includeOptionalAttribute: true)
            : string.Empty;

    private static bool HasMetadataDefaultAttribute(IParameterSymbol parameter)
        => ParameterDefaultValueEmitter.HasDateTimeConstantAttribute(parameter) ||
           ParameterDefaultValueEmitter.HasDecimalConstantAttribute(parameter) ||
           ParameterDefaultValueEmitter.HasDefaultParameterValueAttribute(parameter);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
