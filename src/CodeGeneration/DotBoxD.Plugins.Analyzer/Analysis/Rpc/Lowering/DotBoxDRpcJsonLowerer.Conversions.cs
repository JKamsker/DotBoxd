using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string LowerCast(CastExpressionSyntax cast)
    {
        var targetType = _model.GetTypeInfo(cast, _cancellationToken).Type
                         ?? _model.GetTypeInfo(cast, _cancellationToken).ConvertedType
                         ?? throw new NotSupportedException(
                             $"Server extension cast '{cast}' could not resolve its target type.");
        return ApplyRequiredNumericConversion(
            cast.Expression,
            targetType,
            LowerExpression(cast.Expression),
            $"Server extension cast '{cast}'");
    }

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

    private string ApplyNumericConversion(ExpressionSyntax expression, ITypeSymbol targetType, string lowered)
    {
        var sourceType = _model.GetTypeInfo(expression, _cancellationToken).Type;
        return sourceType is null ? lowered : ApplyNumericConversion(sourceType, targetType, lowered);
    }

    private string ApplyNumericConversion(ITypeSymbol sourceType, ITypeSymbol targetType, string lowered)
        => TryApplyNumericConversion(sourceType, targetType, lowered, out var converted) ? converted : lowered;

    private string ApplyRequiredNumericConversion(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        string lowered,
        string description)
    {
        var typeInfo = _model.GetTypeInfo(expression, _cancellationToken);
        var sourceType = typeInfo.Type;
        if (sourceType is null ||
            SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return lowered;
        }

        if (typeInfo.ConvertedType is not null &&
            SymbolEqualityComparer.Default.Equals(typeInfo.ConvertedType, targetType))
        {
            return lowered;
        }

        if (TryApplyNumericConversion(sourceType, targetType, lowered, out var converted))
        {
            return converted;
        }

        throw new NotSupportedException(
            $"{description} is not supported because it is not a supported numeric widening conversion.");
    }

    private static bool TryApplyNumericConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string lowered,
        out string converted)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            converted = lowered;
            return true;
        }

        if (sourceType.SpecialType == SpecialType.System_Int32 &&
            targetType.SpecialType == SpecialType.System_Int64)
        {
            converted = Call("numeric.toI64", null, lowered);
            return true;
        }

        if (sourceType.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 &&
            targetType.SpecialType is SpecialType.System_Double or SpecialType.System_Single)
        {
            converted = Call("numeric.toF64", null, lowered);
            return true;
        }

        converted = lowered;
        return false;
    }
}
