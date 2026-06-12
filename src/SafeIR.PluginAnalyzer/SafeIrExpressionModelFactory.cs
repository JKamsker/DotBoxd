namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrExpressionModelFactory
{
    public static SafeIrExpressionModel Create(
        ExpressionSyntax expression,
        SafeIrExpressionLoweringContext context)
        => Lower(expression, context);

    private static SafeIrExpressionModel Lower(
        ExpressionSyntax expression,
        SafeIrExpressionLoweringContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (SafeIrConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken) is { } constant)
        {
            return constant;
        }

        return expression switch {
            ParenthesizedExpressionSyntax parenthesized => Lower(parenthesized.Expression, context),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary, context),
            BinaryExpressionSyntax binary => LowerBinary(binary, context),
            IsPatternExpressionSyntax pattern => SafeIrPatternExpressionLowerer.Lower(pattern, context, part => Lower(part, context)),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier.Identifier.ValueText, context.LiveSettings),
            MemberAccessExpressionSyntax member => LowerMemberAccess(member, context),
            InterpolatedStringExpressionSyntax interpolated =>
                SafeIrInterpolatedStringExpressionLowerer.Lower(interpolated, part => Lower(part, context)),
            LiteralExpressionSyntax literal => SafeIrLiteralExpressionLowerer.Lower(literal),
            _ => Unsupported(expression)
        };
    }

    private static SafeIrExpressionModel LowerUnary(
        PrefixUnaryExpressionSyntax unary,
        SafeIrExpressionLoweringContext context)
    {
        if (SafeIrLiteralExpressionLowerer.TryLowerNegative(unary) is { } literal)
        {
            return literal;
        }

        var operand = Lower(unary.Operand, context);
        return unary.Kind() switch {
            SyntaxKind.LogicalNotExpression => Unary(
                SafeIrGenerationNames.Helpers.Not,
                SafeIrGenerationNames.Operators.LogicalNot,
                operand,
                SafeIrGenerationNames.ManifestTypes.Bool,
                SafeIrGenerationNames.ManifestTypes.Bool),
            SyntaxKind.UnaryMinusExpression => SafeIrNumericExpressionLowerer.Unary(
                SafeIrGenerationNames.Helpers.Neg,
                SafeIrGenerationNames.Operators.Minus,
                operand),
            _ => Unsupported(unary)
        };
    }

    private static SafeIrExpressionModel Unary(
        string helper,
        string symbol,
        SafeIrExpressionModel operand,
        string expected,
        string resultType)
    {
        RequireType(operand, expected, $"Unary operator '{symbol}'");
        return new SafeIrExpressionModel($"{helper}({operand.Source})", resultType, operand.Allocates);
    }

    private static SafeIrExpressionModel LowerBinary(
        BinaryExpressionSyntax binary,
        SafeIrExpressionLoweringContext context)
    {
        var left = Lower(binary.Left, context);
        var right = Lower(binary.Right, context);
        SafeIrNumericConstantPromoter.Promote(binary, context, ref left, ref right);
        var allocates = left.Allocates || right.Allocates;

        return binary.Kind() switch {
            SyntaxKind.EqualsExpression => SameType(
                SafeIrGenerationNames.Helpers.Eq,
                SafeIrGenerationNames.Operators.EqualTo,
                left,
                right,
                allocates),
            SyntaxKind.NotEqualsExpression => SameType(
                SafeIrGenerationNames.Helpers.Ne,
                SafeIrGenerationNames.Operators.NotEqualTo,
                left,
                right,
                allocates),
            SyntaxKind.GreaterThanOrEqualExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Ge,
                SafeIrGenerationNames.Operators.GreaterThanOrEqual,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.GreaterThanExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Gt,
                SafeIrGenerationNames.Operators.GreaterThan,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LessThanOrEqualExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Le,
                SafeIrGenerationNames.Operators.LessThanOrEqual,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LessThanExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Lt,
                SafeIrGenerationNames.Operators.LessThan,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LogicalAndExpression => BoolBinary(
                SafeIrGenerationNames.Helpers.And,
                SafeIrGenerationNames.Operators.LogicalAnd,
                left,
                right,
                allocates),
            SyntaxKind.LogicalOrExpression => BoolBinary(
                SafeIrGenerationNames.Helpers.Or,
                SafeIrGenerationNames.Operators.LogicalOr,
                left,
                right,
                allocates),
            SyntaxKind.AddExpression => AddBinary(left, right, allocates),
            SyntaxKind.SubtractExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Sub,
                SafeIrGenerationNames.Operators.Minus,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.MultiplyExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Mul,
                SafeIrGenerationNames.Operators.Multiply,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.DivideExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Div,
                SafeIrGenerationNames.Operators.Divide,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.ModuloExpression => NumericBinary(
                SafeIrGenerationNames.Helpers.Mod,
                SafeIrGenerationNames.Operators.Modulo,
                left,
                right,
                comparison: false,
                allocates),
            _ => Unsupported(binary)
        };
    }

    private static SafeIrExpressionModel AddBinary(
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool allocates)
    {
        if (IsString(left) && IsString(right))
        {
            return new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
                SafeIrGenerationNames.ManifestTypes.String,
                true);
        }

        if (IsString(left) || IsString(right))
        {
            throw new NotSupportedException(
                "Operator '+' requires both operands to be strings or matching numeric operands.");
        }

        return NumericBinary(
            SafeIrGenerationNames.Helpers.Add,
            SafeIrGenerationNames.Operators.Add,
            left,
            right,
            comparison: false,
            allocates);
    }

    private static SafeIrExpressionModel SameType(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool allocates)
    {
        if (!string.Equals(left.Type, right.Type, StringComparison.Ordinal)) {
            throw new NotSupportedException(
                $"Operator '{symbol}' requires operands with the same supported type.");
        }

        return new SafeIrExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            allocates);
    }

    private static SafeIrExpressionModel NumericBinary(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool comparison,
        bool allocates)
        => SafeIrNumericExpressionLowerer.Binary(helper, symbol, left, right, comparison, allocates);

    private static SafeIrExpressionModel BoolBinary(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool allocates)
    {
        RequireType(left, SafeIrGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        RequireType(right, SafeIrGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        return new SafeIrExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            allocates);
    }

    private static SafeIrExpressionModel LowerIdentifier(
        string name,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        for (var i = 0; i < liveSettings.Count; i++) {
            var setting = liveSettings[i];
            if (string.Equals(setting.Name, name, StringComparison.Ordinal)) {
                return new SafeIrExpressionModel(
                    $"{SafeIrGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})",
                    setting.Type,
                    false);
            }
        }

        throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");
    }

    private static SafeIrExpressionModel LowerMemberAccess(
        MemberAccessExpressionSyntax member,
        SafeIrExpressionLoweringContext context)
    {
        var memberName = member.Name.Identifier.ValueText;
        if (member.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal)) {
            for (var i = 0; i < context.EventProperties.Count; i++) {
                var property = context.EventProperties[i];
                if (string.Equals(property.Name, memberName, StringComparison.Ordinal)) {
                    return new SafeIrExpressionModel(
                        $"{SafeIrGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(EventVariable(memberName))})",
                        property.Type,
                        false);
                }
            }

            throw new NotSupportedException($"Unknown event property '{memberName}'.");
        }

        if (member.Expression is ThisExpressionSyntax) {
            return LowerIdentifier(memberName, context.LiveSettings);
        }

        return Unsupported(member);
    }

    public static string EventVariable(string name) => SafeIrGenerationNames.GeneratedVariables.EventPrefix + name;

    private static void RequireType(SafeIrExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal)) {
            throw new NotSupportedException($"{context} requires {expected} operands.");
        }
    }

    private static bool IsString(SafeIrExpressionModel expression)
        => string.Equals(expression.Type, SafeIrGenerationNames.ManifestTypes.String, StringComparison.Ordinal);

    private static SafeIrExpressionModel Unsupported(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
