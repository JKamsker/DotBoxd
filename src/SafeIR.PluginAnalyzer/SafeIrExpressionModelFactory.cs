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
            SyntaxKind.UnaryMinusExpression => Unary(
                SafeIrGenerationNames.Helpers.Neg,
                SafeIrGenerationNames.Operators.Minus,
                operand,
                SafeIrGenerationNames.ManifestTypes.Int,
                SafeIrGenerationNames.ManifestTypes.Int),
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
            SyntaxKind.GreaterThanOrEqualExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Ge,
                SafeIrGenerationNames.Operators.GreaterThanOrEqual,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Bool,
                allocates),
            SyntaxKind.GreaterThanExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Gt,
                SafeIrGenerationNames.Operators.GreaterThan,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Bool,
                allocates),
            SyntaxKind.LessThanOrEqualExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Le,
                SafeIrGenerationNames.Operators.LessThanOrEqual,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Bool,
                allocates),
            SyntaxKind.LessThanExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Lt,
                SafeIrGenerationNames.Operators.LessThan,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Bool,
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
            SyntaxKind.SubtractExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Sub,
                SafeIrGenerationNames.Operators.Minus,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Int,
                allocates),
            SyntaxKind.MultiplyExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Mul,
                SafeIrGenerationNames.Operators.Multiply,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Int,
                allocates),
            SyntaxKind.DivideExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Div,
                SafeIrGenerationNames.Operators.Divide,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Int,
                allocates),
            SyntaxKind.ModuloExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Mod,
                SafeIrGenerationNames.Operators.Modulo,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Int,
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
            throw new NotSupportedException("Operator '+' requires both operands to be strings or both operands to be ints.");
        }

        return IntBinary(
            SafeIrGenerationNames.Helpers.Add,
            SafeIrGenerationNames.Operators.Add,
            left,
            right,
            SafeIrGenerationNames.ManifestTypes.Int,
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

    private static SafeIrExpressionModel IntBinary(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        string resultType,
        bool allocates)
    {
        RequireType(left, SafeIrGenerationNames.ManifestTypes.Int, $"Operator '{symbol}'");
        RequireType(right, SafeIrGenerationNames.ManifestTypes.Int, $"Operator '{symbol}'");
        return new SafeIrExpressionModel($"{helper}({left.Source}, {right.Source})", resultType, allocates);
    }

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
        var setting = liveSettings.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (setting is not null) {
            return new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})",
                setting.Type,
                false);
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
            var property = context.EventProperties.FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal));
            if (property is null) {
                throw new NotSupportedException($"Unknown event property '{memberName}'.");
            }

            return new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(EventVariable(memberName))})",
                property.Type,
                false);
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
