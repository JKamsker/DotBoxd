using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelClientParameterSource
{
    public static string ParameterList(IMethodSymbol method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            parts.Add(Declaration(parameter));
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

    public static string Declaration(IParameterSymbol parameter)
        => TypeName(parameter.Type) + " " + Identifier(parameter.Name) + DefaultClause(parameter);

    public static string Identifier(string name) => "@" + name;

    private static string DefaultClause(IParameterSymbol parameter)
        => parameter.HasExplicitDefaultValue
            ? " = " + DefaultLiteral(parameter)
            : string.Empty;

    private static string DefaultLiteral(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;
        if (parameter.Type.TypeKind == TypeKind.Enum)
        {
            return "(" + TypeName(parameter.Type) + ")" + LiteralReader.ObjectLiteral(value);
        }

        if (parameter.Type.SpecialType == SpecialType.System_Single && value is float number)
        {
            if (float.IsNaN(number))
            {
                return "global::System.Single.NaN";
            }

            if (float.IsPositiveInfinity(number))
            {
                return "global::System.Single.PositiveInfinity";
            }

            if (float.IsNegativeInfinity(number))
            {
                return "global::System.Single.NegativeInfinity";
            }

            return number.ToString(
                DotBoxDGenerationNames.CSharpLiterals.DoubleRoundTripFormat,
                System.Globalization.CultureInfo.InvariantCulture) + "f";
        }

        return LiteralReader.ObjectLiteral(value);
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
