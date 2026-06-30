using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelClientParameterSource
{
    public static string ParameterList(IMethodSymbol method)
    {
        var parts = new List<string>();
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            parts.Add(Declaration(method.Parameters[i], isLast: i == method.Parameters.Length - 1));
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
        => AttributePrefix(parameter) + ParamsModifier(parameter, isLast) + TypeName(parameter.Type) + " " +
           Identifier(parameter.Name) + DefaultClause(parameter);

    public static string Identifier(string name) => "@" + name;

    private static string ParamsModifier(IParameterSymbol parameter, bool isLast)
        => parameter.IsParams && isLast ? "params " : string.Empty;

    private static string DefaultClause(IParameterSymbol parameter)
    {
        if (TryGetDateTimeConstantDefault(parameter, out _))
        {
            return string.Empty;
        }

        return parameter.HasExplicitDefaultValue ? " = " + DefaultLiteral(parameter) : string.Empty;
    }

    private static string AttributePrefix(IParameterSymbol parameter)
        => TryGetDateTimeConstantDefault(parameter, out var dateTime)
            ? "[global::System.Runtime.InteropServices.OptionalAttribute, " +
              "global::System.Runtime.CompilerServices.DateTimeConstantAttribute(" +
              dateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L)] "
            : string.Empty;

    private static bool TryGetDateTimeConstantDefault(IParameterSymbol parameter, out DateTime dateTime)
    {
        if (parameter.HasExplicitDefaultValue &&
            parameter.Type.SpecialType == SpecialType.System_DateTime &&
            parameter.ExplicitDefaultValue is DateTime value)
        {
            dateTime = value;
            return true;
        }

        dateTime = default;
        return false;
    }

    private static string DefaultLiteral(IParameterSymbol parameter)
        => LiteralReader.ObjectDefaultLiteral(parameter.Type, parameter.ExplicitDefaultValue);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
