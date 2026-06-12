namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrExpressionModelFactory
{
    public static SafeIrExpressionModel Create(
        ExpressionSyntax expression,
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
        => Lower(expression, eventParameterName, eventProperties, liveSettings);

    private static SafeIrExpressionModel Lower(
        ExpressionSyntax expression,
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
        => expression switch {
            ParenthesizedExpressionSyntax parenthesized => Lower(parenthesized.Expression, eventParameterName, eventProperties, liveSettings),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary, eventParameterName, eventProperties, liveSettings),
            BinaryExpressionSyntax binary => LowerBinary(binary, eventParameterName, eventProperties, liveSettings),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier.Identifier.ValueText, liveSettings),
            MemberAccessExpressionSyntax member => LowerMemberAccess(member, eventParameterName, eventProperties, liveSettings),
            LiteralExpressionSyntax literal => LowerLiteral(literal),
            _ => Unsupported(expression)
        };

    private static SafeIrExpressionModel LowerUnary(
        PrefixUnaryExpressionSyntax unary,
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var operand = Lower(unary.Operand, eventParameterName, eventProperties, liveSettings);
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
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var left = Lower(binary.Left, eventParameterName, eventProperties, liveSettings);
        var right = Lower(binary.Right, eventParameterName, eventProperties, liveSettings);
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
            SyntaxKind.AddExpression => IntBinary(
                SafeIrGenerationNames.Helpers.Add,
                SafeIrGenerationNames.Operators.Add,
                left,
                right,
                SafeIrGenerationNames.ManifestTypes.Int,
                allocates),
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
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var memberName = member.Name.Identifier.ValueText;
        if (member.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, eventParameterName, StringComparison.Ordinal)) {
            var property = eventProperties.FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal));
            if (property is null) {
                throw new NotSupportedException($"Unknown event property '{memberName}'.");
            }

            return new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(EventVariable(memberName))})",
                property.Type,
                false);
        }

        if (member.Expression is ThisExpressionSyntax) {
            return LowerIdentifier(memberName, liveSettings);
        }

        return Unsupported(member);
    }

    private static SafeIrExpressionModel LowerLiteral(LiteralExpressionSyntax literal)
        => literal.Token.Value switch {
            bool value => new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.Bool}({(value ? "true" : "false")})",
                SafeIrGenerationNames.ManifestTypes.Bool,
                false),
            int value => new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})",
                SafeIrGenerationNames.ManifestTypes.Int,
                false),
            long value => new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(value)})",
                SafeIrGenerationNames.ManifestTypes.Long,
                false),
            double value when !double.IsNaN(value) && !double.IsInfinity(value) => new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})",
                SafeIrGenerationNames.ManifestTypes.Double,
                false),
            string value => new SafeIrExpressionModel(
                $"{SafeIrGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
                SafeIrGenerationNames.ManifestTypes.String,
                true),
            _ => Unsupported(literal)
        };

    public static string EventVariable(string name) => "e_" + name;

    private static void RequireType(SafeIrExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal)) {
            throw new NotSupportedException($"{context} requires {expected} operands.");
        }
    }

    private static SafeIrExpressionModel Unsupported(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
