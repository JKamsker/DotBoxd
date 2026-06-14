namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrInvocationExpressionLowerer
{
    private const int EqualsArgumentCount = 1;
    private const int EqualsValueArgumentIndex = 0;
    private const int SubstringArgumentCount = 2;
    private const int SubstringStartIndexArgumentIndex = 0;
    private const int SubstringLengthArgumentIndex = 1;
    private const string EqualsMethodName = "Equals";
    private const string SubstringMethodName = "Substring";
    private const string StartIndexArgumentName = "startIndex";
    private const string LengthArgumentName = "length";
    private const string EqualsArgumentCountMessage =
        "Instance Equals calls must have exactly one argument.";
    private const string EqualsOperandTypeMessage =
        "Instance Equals calls require operands with the same supported type.";
    private const string SubstringArgumentCountMessage =
        "Substring calls must have startIndex and length arguments.";

    public static SafeIrExpressionModel Lower(
        InvocationExpressionSyntax invocation,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (SafeIrHostBindingExpressionLowerer.TryLower(invocation, context, lowerExpression) is { } hostCall)
        {
            return hostCall;
        }

        if (SafeIrKernelMethodInliner.TryInline(invocation, context, lowerExpression) is { } inlined)
        {
            return inlined;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax member &&
            string.Equals(member.Name.Identifier.ValueText, EqualsMethodName, StringComparison.Ordinal))
        {
            return LowerEquals(invocation, member, lowerExpression);
        }

        if (invocation.Expression is MemberAccessExpressionSyntax substringMember &&
            string.Equals(substringMember.Name.Identifier.ValueText, SubstringMethodName, StringComparison.Ordinal))
        {
            return LowerSubstring(invocation, substringMember, lowerExpression);
        }

        throw new NotSupportedException($"Unsupported plugin invocation '{invocation}'.");
    }

    private static SafeIrExpressionModel LowerEquals(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != EqualsArgumentCount)
        {
            throw new NotSupportedException(EqualsArgumentCountMessage);
        }

        var receiver = lowerExpression(member.Expression);
        var value = lowerExpression(arguments[EqualsValueArgumentIndex].Expression);
        if (!string.Equals(receiver.Type, value.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(EqualsOperandTypeMessage);
        }

        return new SafeIrExpressionModel(
            $"{EqualsHelper(receiver)}({receiver.Source}, {value.Source})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            receiver.Allocates || value.Allocates);
    }

    private static SafeIrExpressionModel LowerSubstring(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var receiver = lowerExpression(member.Expression);
        RequireType(receiver, SafeIrGenerationNames.ManifestTypes.String, "Substring receiver");
        var (startIndex, length) = SubstringArguments(invocation, lowerExpression);
        RequireType(startIndex, SafeIrGenerationNames.ManifestTypes.Int, "Substring startIndex");
        RequireType(length, SafeIrGenerationNames.ManifestTypes.Int, "Substring length");
        return new SafeIrExpressionModel(
            $"{SafeIrGenerationNames.Helpers.StringSubstring}({receiver.Source}, {startIndex.Source}, {length.Source})",
            SafeIrGenerationNames.ManifestTypes.String,
            true);
    }

    private static (SafeIrExpressionModel StartIndex, SafeIrExpressionModel Length) SubstringArguments(
        InvocationExpressionSyntax invocation,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != SubstringArgumentCount)
        {
            throw new NotSupportedException(SubstringArgumentCountMessage);
        }

        ExpressionSyntax? startIndex = null;
        ExpressionSyntax? length = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText;
            if (name is null)
            {
                AssignByPosition(i, argument.Expression, ref startIndex, ref length);
                continue;
            }

            AssignByName(name, argument.Expression, ref startIndex, ref length);
        }

        return startIndex is not null && length is not null
            ? (lowerExpression(startIndex), lowerExpression(length))
            : throw new NotSupportedException(SubstringArgumentCountMessage);
    }

    private static void AssignByPosition(
        int index,
        ExpressionSyntax expression,
        ref ExpressionSyntax? startIndex,
        ref ExpressionSyntax? length)
    {
        if (index == SubstringStartIndexArgumentIndex)
        {
            Assign(StartIndexArgumentName, expression, ref startIndex);
            return;
        }

        if (index == SubstringLengthArgumentIndex)
        {
            Assign(LengthArgumentName, expression, ref length);
            return;
        }

        throw new NotSupportedException(SubstringArgumentCountMessage);
    }

    private static void AssignByName(
        string name,
        ExpressionSyntax expression,
        ref ExpressionSyntax? startIndex,
        ref ExpressionSyntax? length)
    {
        if (string.Equals(name, StartIndexArgumentName, StringComparison.Ordinal))
        {
            Assign(name, expression, ref startIndex);
            return;
        }

        if (string.Equals(name, LengthArgumentName, StringComparison.Ordinal))
        {
            Assign(name, expression, ref length);
            return;
        }

        throw new NotSupportedException(SubstringArgumentCountMessage);
    }

    private static void Assign(string name, ExpressionSyntax expression, ref ExpressionSyntax? slot)
    {
        if (slot is not null)
        {
            throw new NotSupportedException($"Substring has duplicate argument '{name}'.");
        }

        slot = expression;
    }

    private static string EqualsHelper(SafeIrExpressionModel receiver)
        => string.Equals(receiver.Type, SafeIrGenerationNames.ManifestTypes.String, StringComparison.Ordinal)
            ? SafeIrGenerationNames.Helpers.StringEquals
            : SafeIrGenerationNames.Helpers.Eq;

    private static void RequireType(SafeIrExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"{context} must lower to {expected}.");
        }
    }
}
