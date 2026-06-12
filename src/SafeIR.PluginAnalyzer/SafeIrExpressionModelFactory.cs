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
            ParenthesizedExpressionSyntax parenthesized => Lower(
                parenthesized.Expression,
                eventParameterName,
                eventProperties,
                liveSettings),
            PrefixUnaryExpressionSyntax unary => LowerUnary(
                unary,
                eventParameterName,
                eventProperties,
                liveSettings),
            BinaryExpressionSyntax binary => LowerBinary(
                binary,
                eventParameterName,
                eventProperties,
                liveSettings),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier.Identifier.ValueText, liveSettings),
            MemberAccessExpressionSyntax member => LowerMemberAccess(
                member,
                eventParameterName,
                eventProperties,
                liveSettings),
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
            SyntaxKind.LogicalNotExpression => Unary("Not", "!", operand, "bool", "bool"),
            SyntaxKind.UnaryMinusExpression => Unary("Neg", "-", operand, "int", "int"),
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
        return new SafeIrExpressionModel(
            $"{helper}({operand.Source})",
            resultType,
            operand.Allocates);
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
            SyntaxKind.EqualsExpression => SameType("Eq", "==", left, right, allocates),
            SyntaxKind.NotEqualsExpression => SameType("Ne", "!=", left, right, allocates),
            SyntaxKind.GreaterThanOrEqualExpression => IntBinary("Ge", ">=", left, right, "bool", allocates),
            SyntaxKind.GreaterThanExpression => IntBinary("Gt", ">", left, right, "bool", allocates),
            SyntaxKind.LessThanOrEqualExpression => IntBinary("Le", "<=", left, right, "bool", allocates),
            SyntaxKind.LessThanExpression => IntBinary("Lt", "<", left, right, "bool", allocates),
            SyntaxKind.LogicalAndExpression => BoolBinary("And", "&&", left, right, allocates),
            SyntaxKind.LogicalOrExpression => BoolBinary("Or", "||", left, right, allocates),
            SyntaxKind.AddExpression => IntBinary("Add", "+", left, right, "int", allocates),
            SyntaxKind.SubtractExpression => IntBinary("Sub", "-", left, right, "int", allocates),
            SyntaxKind.MultiplyExpression => IntBinary("Mul", "*", left, right, "int", allocates),
            SyntaxKind.DivideExpression => IntBinary("Div", "/", left, right, "int", allocates),
            SyntaxKind.ModuloExpression => IntBinary("Mod", "%", left, right, "int", allocates),
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

        return new SafeIrExpressionModel($"{helper}({left.Source}, {right.Source})", "bool", allocates);
    }

    private static SafeIrExpressionModel IntBinary(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        string resultType,
        bool allocates)
    {
        RequireType(left, "int", $"Operator '{symbol}'");
        RequireType(right, "int", $"Operator '{symbol}'");
        return new SafeIrExpressionModel($"{helper}({left.Source}, {right.Source})", resultType, allocates);
    }

    private static SafeIrExpressionModel BoolBinary(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool allocates)
    {
        RequireType(left, "bool", $"Operator '{symbol}'");
        RequireType(right, "bool", $"Operator '{symbol}'");
        return new SafeIrExpressionModel($"{helper}({left.Source}, {right.Source})", "bool", allocates);
    }

    private static SafeIrExpressionModel LowerIdentifier(
        string name,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var setting = liveSettings.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (setting is not null) {
            return new SafeIrExpressionModel($"Var({LiteralReader.StringLiteral(name)})", setting.Type, false);
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
                $"Var({LiteralReader.StringLiteral(EventVariable(memberName))})",
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
            bool value => new SafeIrExpressionModel($"Bool({(value ? "true" : "false")})", "bool", false),
            int value => new SafeIrExpressionModel($"I32({LiteralReader.ObjectLiteral(value)})", "int", false),
            long value => new SafeIrExpressionModel($"I64({LiteralReader.ObjectLiteral(value)})", "long", false),
            double value when !double.IsNaN(value) && !double.IsInfinity(value) => new SafeIrExpressionModel(
                $"F64({LiteralReader.ObjectLiteral(value)})",
                "double",
                false),
            string value => new SafeIrExpressionModel($"Str({LiteralReader.StringLiteral(value)})", "string", true),
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
