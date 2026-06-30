using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDPatternExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        IsPatternExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var value = lowerExpression(expression.Expression);
        return LowerPattern(value, expression.Expression, expression.Pattern, context, lowerExpression);
    }

    public static bool TryLowerDeclarationPattern(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out DotBoxDExpressionModel lowered,
        out string captureName,
        out PatternCaptureBinding capture)
    {
        var unwrapped = Unwrap(expression);
        if (unwrapped is IsPatternExpressionSyntax
            {
                Pattern: DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax } declaration
            } pattern)
        {
            var value = lowerExpression(pattern.Expression);
            lowered = LowerDeclaration(value, pattern.Expression, declaration, context, out var name, out var binding);
            captureName = name!;
            capture = binding!;
            return true;
        }

        if (unwrapped is IsPatternExpressionSyntax
            {
                Pattern: RecursivePatternSyntax { Designation: SingleVariableDesignationSyntax } recursive
            } recursivePattern)
        {
            var value = lowerExpression(recursivePattern.Expression);
            lowered = LowerRecursive(value, recursivePattern.Expression, recursive, context, lowerExpression, out var name, out var binding);
            captureName = name!;
            capture = binding!;
            return true;
        }

        lowered = null!;
        captureName = string.Empty;
        capture = null!;
        return false;
    }

    public static DotBoxDExpressionModel LowerIsTypeExpression(
        BinaryExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (expression.Kind() != SyntaxKind.IsExpression ||
            expression.Right is not TypeSyntax typeSyntax)
        {
            throw new NotSupportedException($"Unsupported plugin pattern '{expression}'.");
        }

        var value = lowerExpression(expression.Left);
        return LowerSubtypeTest(value, expression.Left, typeSyntax, expression, context, out _);
    }

    private static DotBoxDExpressionModel LowerPattern(
        DotBoxDExpressionModel value,
        ExpressionSyntax valueSyntax,
        PatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
        => pattern switch
        {
            ParenthesizedPatternSyntax parenthesized =>
                LowerPattern(value, valueSyntax, parenthesized.Pattern, context, lowerExpression),
            DeclarationPatternSyntax declaration =>
                LowerDeclaration(value, valueSyntax, declaration, context, out _, out _),
            RecursivePatternSyntax recursive =>
                LowerRecursive(value, valueSyntax, recursive, context, lowerExpression, out _, out _),
            TypePatternSyntax type =>
                LowerSubtypeTest(value, valueSyntax, type.Type, type, context, out _),
            ConstantPatternSyntax constant =>
                LowerConstant(value, constant, context, lowerExpression),
            RelationalPatternSyntax relational =>
                LowerRelational(value, relational, context, lowerExpression),
            UnaryPatternSyntax unary when unary.Kind() == SyntaxKind.NotPattern =>
                LowerNot(value, valueSyntax, unary, context, lowerExpression),
            _ => Unsupported(pattern)
        };

    private static DotBoxDExpressionModel LowerDeclaration(
        DotBoxDExpressionModel value,
        ExpressionSyntax valueSyntax,
        DeclarationPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        out string? captureName,
        out PatternCaptureBinding? capture)
    {
        var lowered = LowerSubtypeTest(value, valueSyntax, pattern.Type, pattern, context, out var subtype);
        captureName = null;
        capture = null;
        if (pattern.Designation is SingleVariableDesignationSyntax designation)
        {
            captureName = designation.Identifier.ValueText;
            capture = new PatternCaptureBinding(value, subtype);
        }
        else if (pattern.Designation is not null and not DiscardDesignationSyntax)
        {
            throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
        }

        return lowered;
    }

    private static DotBoxDExpressionModel LowerSubtypeTest(
        DotBoxDExpressionModel value,
        ExpressionSyntax valueSyntax,
        TypeSyntax typeSyntax,
        CSharpSyntaxNode pattern,
        DotBoxDExpressionLoweringContext context,
        out INamedTypeSymbol subtype)
    {
        subtype = null!;
        if (context.SemanticModel.GetTypeInfo(valueSyntax, context.CancellationToken).Type is not { } handleType ||
            context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type is not INamedTypeSymbol resolvedSubtype ||
            !PolymorphicHandleMetadataReader.TryResolve(handleType, out var handle) ||
            !handle.TrySubtype(resolvedSubtype, out var subtypeMetadata) ||
            !string.Equals(value.Type, handle.KeyManifestTag, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
        }

        subtype = resolvedSubtype;
        CollectSubtypeRequirements(context, subtypeMetadata);
        return new DotBoxDExpressionModel(
            $"new {DotBoxDGenerationNames.TypeNames.GlobalCallExpression}(" +
            $"{LiteralReader.StringLiteral(subtypeMetadata.DiscriminatorBindingId)}, [{value.Source}], null, Span)",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            value.Allocates);
    }

    private static DotBoxDExpressionModel LowerConstant(
        DotBoxDExpressionModel value,
        ConstantPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var constant = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        if (!string.Equals(value.Type, constant.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Pattern constant '{pattern.Expression}' must match the input expression type.");
        }

        return DotBoxDEqualityExpressionLowerer.Lower(
            value,
            constant,
            negate: false,
            value.Allocates || constant.Allocates,
            leftType: null,
            rightType: null);
    }

    private static DotBoxDExpressionModel LowerRelational(
        DotBoxDExpressionModel value,
        RelationalPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (!DotBoxDNumericExpressionLowerer.IsNumeric(value))
        {
            throw new NotSupportedException("Relational patterns require numeric input expressions.");
        }

        var comparand = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        var (helper, symbol) = RelationalOperator(pattern);
        return DotBoxDNumericExpressionLowerer.Binary(
            helper,
            symbol,
            value,
            comparand,
            comparison: true,
            value.Allocates || comparand.Allocates);
    }

    private static DotBoxDExpressionModel LowerNot(
        DotBoxDExpressionModel value,
        ExpressionSyntax valueSyntax,
        UnaryPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (ContainsDeclarationPattern(pattern.Pattern))
        {
            throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
        }

        var inner = LowerPattern(value, valueSyntax, pattern.Pattern, context, lowerExpression);
        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.Not}({inner.Source})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            inner.Allocates);
    }

    private static DotBoxDExpressionModel LowerPatternValue(
        ExpressionSyntax expression,
        string targetType,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (DotBoxDGenerationNames.ManifestTypes.IsNumeric(targetType) &&
            DotBoxDNumericConstantPromoter.TryPromoteConstant(expression, context, targetType) is { } promoted)
        {
            return promoted;
        }

        return lowerExpression(expression);
    }

    private static (string Helper, string Symbol) RelationalOperator(RelationalPatternSyntax pattern)
        => pattern.OperatorToken.Kind() switch
        {
            SyntaxKind.GreaterThanEqualsToken => (
                DotBoxDGenerationNames.Helpers.Ge,
                DotBoxDGenerationNames.Operators.GreaterThanOrEqual),
            SyntaxKind.GreaterThanToken => (
                DotBoxDGenerationNames.Helpers.Gt,
                DotBoxDGenerationNames.Operators.GreaterThan),
            SyntaxKind.LessThanEqualsToken => (
                DotBoxDGenerationNames.Helpers.Le,
                DotBoxDGenerationNames.Operators.LessThanOrEqual),
            SyntaxKind.LessThanToken => (
                DotBoxDGenerationNames.Helpers.Lt,
                DotBoxDGenerationNames.Operators.LessThan),
            _ => throw new NotSupportedException(
                $"Unsupported relational pattern operator '{pattern.OperatorToken.ValueText}'.")
        };

    private static DotBoxDExpressionModel Unsupported(PatternSyntax pattern)
        => throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");

    public static bool ContainsDeclarationPattern(ExpressionSyntax expression)
        => expression.DescendantNodesAndSelf().Any(static node => node is DeclarationPatternSyntax or RecursivePatternSyntax
        {
            Designation: not null and not DiscardDesignationSyntax
        });

    private static bool ContainsDeclarationPattern(PatternSyntax pattern)
        => pattern.DescendantNodesAndSelf().Any(static node => node is DeclarationPatternSyntax or RecursivePatternSyntax
        {
            Designation: not null and not DiscardDesignationSyntax
        });

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current;
    }

    private static void CollectSubtypeRequirements(
        DotBoxDExpressionLoweringContext context,
        HandleSubtypeMetadata subtype)
    {
        context.Capabilities?.Add(subtype.Capability);
        context.Effects?.Add(DotBoxDGenerationNames.Effects.Cpu);
        context.Effects?.Add(DotBoxDGenerationNames.Effects.HostStateRead);
    }
}
