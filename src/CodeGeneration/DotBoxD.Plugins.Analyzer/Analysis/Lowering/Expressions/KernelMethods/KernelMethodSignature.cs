using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class KernelMethodSignature
{
    public static string Create(IMethodSymbol method)
    {
        var parameters = string.Join(
            ",",
            method.Parameters.Select(static parameter => TypeName(parameter.Type)));
        return TypeName(method.ReturnType) + " " + method.MetadataName + "(" + parameters + ")";
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
