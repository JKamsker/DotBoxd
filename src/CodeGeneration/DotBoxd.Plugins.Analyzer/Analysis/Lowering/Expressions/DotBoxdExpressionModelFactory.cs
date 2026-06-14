namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdExpressionModelFactory
{
    public static DotBoxdExpressionModel Create(
        ExpressionSyntax expression,
        DotBoxdExpressionLoweringContext context)
        => Lower(expression, context);

    private static DotBoxdExpressionModel Lower(
        ExpressionSyntax expression,
        DotBoxdExpressionLoweringContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (DotBoxdConstantExpressionLowerer.TryLower(
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
            InvocationExpressionSyntax invocation =>
                DotBoxdInvocationExpressionLowerer.Lower(invocation, context, part => Lower(part, context)),
            IsPatternExpressionSyntax pattern => DotBoxdPatternExpressionLowerer.Lower(pattern, context, part => Lower(part, context)),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier.Identifier.ValueText, context),
            MemberAccessExpressionSyntax member
                when DotBoxdStringExpressionLowerer.TryLowerMember(member, context, part => Lower(part, context)) is { } lowered =>
                lowered,
            MemberAccessExpressionSyntax member => LowerMemberAccess(member, context),
            InterpolatedStringExpressionSyntax interpolated =>
                DotBoxdInterpolatedStringExpressionLowerer.Lower(interpolated, part => Lower(part, context)),
            LiteralExpressionSyntax literal => DotBoxdLiteralExpressionLowerer.Lower(literal),
            _ => Unsupported(expression)
        };
    }

    private static DotBoxdExpressionModel LowerUnary(
        PrefixUnaryExpressionSyntax unary,
        DotBoxdExpressionLoweringContext context)
    {
        if (DotBoxdLiteralExpressionLowerer.TryLowerNegative(unary) is { } literal)
        {
            return literal;
        }

        var operand = Lower(unary.Operand, context);
        return unary.Kind() switch {
            SyntaxKind.LogicalNotExpression => Unary(
                DotBoxdGenerationNames.Helpers.Not,
                DotBoxdGenerationNames.Operators.LogicalNot,
                operand,
                DotBoxdGenerationNames.ManifestTypes.Bool,
                DotBoxdGenerationNames.ManifestTypes.Bool),
            SyntaxKind.UnaryMinusExpression => DotBoxdNumericExpressionLowerer.Unary(
                DotBoxdGenerationNames.Helpers.Neg,
                DotBoxdGenerationNames.Operators.Minus,
                operand),
            _ => Unsupported(unary)
        };
    }

    private static DotBoxdExpressionModel Unary(
        string helper,
        string symbol,
        DotBoxdExpressionModel operand,
        string expected,
        string resultType)
    {
        RequireType(operand, expected, $"Unary operator '{symbol}'");
        return new DotBoxdExpressionModel($"{helper}({operand.Source})", resultType, operand.Allocates);
    }

    private static DotBoxdExpressionModel LowerBinary(
        BinaryExpressionSyntax binary,
        DotBoxdExpressionLoweringContext context)
    {
        var left = Lower(binary.Left, context);
        var right = Lower(binary.Right, context);
        DotBoxdNumericConstantPromoter.Promote(binary, context, ref left, ref right);
        var allocates = left.Allocates || right.Allocates;

        return binary.Kind() switch {
            SyntaxKind.EqualsExpression => DotBoxdEqualityExpressionLowerer.Lower(
                left,
                right,
                negate: false,
                allocates),
            SyntaxKind.NotEqualsExpression => DotBoxdEqualityExpressionLowerer.Lower(
                left,
                right,
                negate: true,
                allocates),
            SyntaxKind.GreaterThanOrEqualExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Ge,
                DotBoxdGenerationNames.Operators.GreaterThanOrEqual,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.GreaterThanExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Gt,
                DotBoxdGenerationNames.Operators.GreaterThan,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LessThanOrEqualExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Le,
                DotBoxdGenerationNames.Operators.LessThanOrEqual,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LessThanExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Lt,
                DotBoxdGenerationNames.Operators.LessThan,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LogicalAndExpression => BoolBinary(
                DotBoxdGenerationNames.Helpers.And,
                DotBoxdGenerationNames.Operators.LogicalAnd,
                left,
                right,
                allocates),
            SyntaxKind.LogicalOrExpression => BoolBinary(
                DotBoxdGenerationNames.Helpers.Or,
                DotBoxdGenerationNames.Operators.LogicalOr,
                left,
                right,
                allocates),
            SyntaxKind.AddExpression => AddBinary(left, right, allocates),
            SyntaxKind.SubtractExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Sub,
                DotBoxdGenerationNames.Operators.Minus,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.MultiplyExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Mul,
                DotBoxdGenerationNames.Operators.Multiply,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.DivideExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Div,
                DotBoxdGenerationNames.Operators.Divide,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.ModuloExpression => NumericBinary(
                DotBoxdGenerationNames.Helpers.Mod,
                DotBoxdGenerationNames.Operators.Modulo,
                left,
                right,
                comparison: false,
                allocates),
            _ => Unsupported(binary)
        };
    }

    private static DotBoxdExpressionModel AddBinary(
        DotBoxdExpressionModel left,
        DotBoxdExpressionModel right,
        bool allocates)
    {
        if (IsString(left) && IsString(right))
        {
            return new DotBoxdExpressionModel(
                $"{DotBoxdGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
                DotBoxdGenerationNames.ManifestTypes.String,
                true);
        }

        if (IsString(left) || IsString(right))
        {
            throw new NotSupportedException(
                "Operator '+' requires both operands to be strings or matching numeric operands.");
        }

        return NumericBinary(
            DotBoxdGenerationNames.Helpers.Add,
            DotBoxdGenerationNames.Operators.Add,
            left,
            right,
            comparison: false,
            allocates);
    }

    private static DotBoxdExpressionModel NumericBinary(
        string helper,
        string symbol,
        DotBoxdExpressionModel left,
        DotBoxdExpressionModel right,
        bool comparison,
        bool allocates)
        => DotBoxdNumericExpressionLowerer.Binary(helper, symbol, left, right, comparison, allocates);

    private static DotBoxdExpressionModel BoolBinary(
        string helper,
        string symbol,
        DotBoxdExpressionModel left,
        DotBoxdExpressionModel right,
        bool allocates)
    {
        RequireType(left, DotBoxdGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        RequireType(right, DotBoxdGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        return new DotBoxdExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            DotBoxdGenerationNames.ManifestTypes.Bool,
            allocates);
    }

    private static DotBoxdExpressionModel LowerIdentifier(
        string name,
        DotBoxdExpressionLoweringContext context)
    {
        // Inside an inlined [KernelMethod] body: a reference to one of the method's parameters resolves to
        // the already-lowered IR of the call-site argument. Checked first so a parameter shadows any
        // same-named live setting (correct C# scoping).
        if (context.InlinedBindings is { } bindings &&
            bindings.TryGetValue(name, out var bound))
        {
            return bound;
        }

        // A Select-projected element: inline its already-lowered IR wherever the downstream lambda
        // names it (compile-time substitution; the projection's event-property refs ride along).
        if (context.ProjectedElementName is { } projectedName &&
            string.Equals(projectedName, name, StringComparison.Ordinal))
        {
            return context.ProjectedElement!;
        }

        var liveSettings = context.LiveSettings;
        for (var i = 0; i < liveSettings.Count; i++) {
            var setting = liveSettings[i];
            if (string.Equals(setting.Name, name, StringComparison.Ordinal)) {
                return new DotBoxdExpressionModel(
                    $"{DotBoxdGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})",
                    setting.Type,
                    false);
            }
        }

        throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");
    }

    private static DotBoxdExpressionModel LowerMemberAccess(
        MemberAccessExpressionSyntax member,
        DotBoxdExpressionLoweringContext context)
    {
        var memberName = member.Name.Identifier.ValueText;
        if (member.Expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal)) {
            for (var i = 0; i < context.EventProperties.Count; i++) {
                var property = context.EventProperties[i];
                if (string.Equals(property.Name, memberName, StringComparison.Ordinal)) {
                    CollectEventPropertyCapability(member, context);
                    return new DotBoxdExpressionModel(
                        $"{DotBoxdGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(EventVariable(memberName))})",
                        property.Type,
                        false);
                }
            }

            throw new NotSupportedException($"Unknown event property '{memberName}'.");
        }

        if (member.Expression is ThisExpressionSyntax) {
            return LowerIdentifier(memberName, context);
        }

        return Unsupported(member);
    }

    /// <summary>
    /// Records the capability gating a <c>[Capability]</c>-annotated event property so reading it
    /// contributes to the kernel's required capabilities (deny-at-install if the policy lacks it).
    /// Unannotated properties stay ungated.
    /// </summary>
    private static void CollectEventPropertyCapability(
        MemberAccessExpressionSyntax member,
        DotBoxdExpressionLoweringContext context)
    {
        if (context.Capabilities is null ||
            context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol is not IPropertySymbol property)
        {
            return;
        }

        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxdGenerationNames.Metadata.CapabilityAttribute,
                    StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string id &&
                !string.IsNullOrEmpty(id))
            {
                context.Capabilities.Add(id);
            }
        }
    }

    public static string EventVariable(string name) => DotBoxdGenerationNames.GeneratedVariables.EventPrefix + name;

    private static void RequireType(DotBoxdExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal)) {
            throw new NotSupportedException($"{context} requires {expected} operands.");
        }
    }

    internal static bool IsString(DotBoxdExpressionModel expression)
        => string.Equals(expression.Type, DotBoxdGenerationNames.ManifestTypes.String, StringComparison.Ordinal);

    private static DotBoxdExpressionModel Unsupported(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
