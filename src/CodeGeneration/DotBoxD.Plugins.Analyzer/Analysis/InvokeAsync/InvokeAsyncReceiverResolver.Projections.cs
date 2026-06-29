using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncReceiverResolver
{
    private static bool TryResolveGeneratedBuilderProjection(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (receiver is not MemberAccessExpressionSyntax access ||
            !TryTupleElementIndex(access, model, cancellationToken, out var index) ||
            TupleElementInitializer(access.Expression, index, model, cancellationToken) is not { } initializer ||
            !TryFacadeNameFromBuilderInitializer(initializer, out var facadeName))
        {
            return false;
        }

        return TryFindGeneratedFacade(model.Compilation, facadeName, cancellationToken, out receiverType);
    }

    private static bool TryTupleElementIndex(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        out int index)
    {
        if (TryTupleItemNameIndex(access.Name.Identifier.ValueText, out index))
        {
            return true;
        }

        if (model.GetSymbolInfo(access, cancellationToken).Symbol is IFieldSymbol
            {
                CorrespondingTupleField.Name: { } itemName
            })
        {
            return TryTupleItemNameIndex(itemName, out index);
        }

        return false;
    }

    private static bool TryTupleItemNameIndex(string name, out int index)
    {
        index = -1;
        if (!name.StartsWith("Item", StringComparison.Ordinal) ||
            !int.TryParse(name.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out var oneBasedIndex) ||
            oneBasedIndex <= 0)
        {
            return false;
        }

        index = oneBasedIndex - 1;
        return true;
    }

    private static ExpressionSyntax? TupleElementInitializer(
        ExpressionSyntax expression,
        int index,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var tuple = expression as TupleExpressionSyntax;
        if (tuple is null &&
            HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            tuple = HookChainAliasResolver.UnwrapTransparentExpression(initializer) as TupleExpressionSyntax;
        }

        return tuple is not null && index < tuple.Arguments.Count
            ? tuple.Arguments[index].Expression
            : null;
    }
}
