using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static GeneratedRemoteHookChainTarget? TargetFromAnonymousObjectPropertyAccess(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
        => AnonymousObjectPropertyInitializer(access, model, cancellationToken) is { } initializer
            ? RegistryTarget(initializer, model, cancellationToken, depth + 1)
            : null;

    private static ExpressionSyntax? AnonymousObjectPropertyInitializer(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var memberName = access.Name.Identifier.ValueText;
        var expression = HookChainAliasResolver.UnwrapTransparentExpression(access.Expression);
        var creation = expression as AnonymousObjectCreationExpressionSyntax;
        if (creation is null &&
            HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            creation = HookChainAliasResolver.UnwrapTransparentExpression(initializer)
                as AnonymousObjectCreationExpressionSyntax;
        }

        if (creation is null)
        {
            return null;
        }

        foreach (var memberInitializer in creation.Initializers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                AnonymousObjectMemberName(memberInitializer),
                memberName,
                StringComparison.Ordinal))
            {
                return memberInitializer.Expression;
            }
        }

        return null;
    }

    private static string? AnonymousObjectMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        if (initializer.NameEquals is { Name.Identifier.ValueText: { } explicitName })
        {
            return explicitName;
        }

        return initializer.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: SimpleNameSyntax name } => name.Identifier.ValueText,
            _ => null
        };
    }

    private static GeneratedRemoteHookChainTarget? TargetFromTupleElementAccess(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (!TryTupleElementIndex(access, model, cancellationToken, out var index))
        {
            return null;
        }

        if (TupleElementInitializer(access.Expression, index, model, cancellationToken) is { } initializer &&
            RegistryTarget(initializer, model, cancellationToken, depth + 1) is { } target)
        {
            return target;
        }

        return TupleElementTypeSyntax(access.Expression, index, model, cancellationToken) is { } typeSyntax
            ? TargetFromOwnedGeneratedRegistryType(typeSyntax, model, cancellationToken)
            : null;
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

    private static TypeSyntax? TupleElementTypeSyntax(
        ExpressionSyntax expression,
        int index,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var typeSyntax = DeclaredTypeSyntax(expression, model, cancellationToken);
        if (typeSyntax is null &&
            HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            typeSyntax = DeclaredTypeSyntax(initializer, model, cancellationToken);
        }

        return typeSyntax is TupleTypeSyntax tuple && index < tuple.Elements.Count
            ? tuple.Elements[index].Type
            : null;
    }
}
