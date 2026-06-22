using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string ApplyNumericConversion(ExpressionSyntax expression, string lowered)
    {
        var type = _model.GetTypeInfo(expression, _cancellationToken);
        if (type.Type is null ||
            type.ConvertedType is null)
        {
            return lowered;
        }

        return ApplyNumericConversion(type.Type, type.ConvertedType, lowered);
    }

    private string ApplyNumericConversion(ITypeSymbol sourceType, ITypeSymbol targetType, string lowered)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return lowered;
        }

        if (sourceType.SpecialType == SpecialType.System_Int32 &&
            targetType.SpecialType == SpecialType.System_Int64)
        {
            return Call("numeric.toI64", null, lowered);
        }

        if (sourceType.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 &&
            targetType.SpecialType == SpecialType.System_Double)
        {
            return Call("numeric.toF64", null, lowered);
        }

        return lowered;
    }
}
