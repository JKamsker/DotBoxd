namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdInvocationExpressionLowerer
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

    public static DotBoxdExpressionModel Lower(
        InvocationExpressionSyntax invocation,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (DotBoxdHostBindingExpressionLowerer.TryLower(invocation, context, lowerExpression) is { } hostCall)
        {
            return hostCall;
        }

        if (DotBoxdKernelMethodInliner.TryInline(invocation, context, lowerExpression) is { } inlined)
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

    private static DotBoxdExpressionModel LowerEquals(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
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

        return new DotBoxdExpressionModel(
            $"{EqualsHelper(receiver)}({receiver.Source}, {value.Source})",
            DotBoxdGenerationNames.ManifestTypes.Bool,
            receiver.Allocates || value.Allocates);
    }

    private static DotBoxdExpressionModel LowerSubstring(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        var receiver = lowerExpression(member.Expression);
        RequireType(receiver, DotBoxdGenerationNames.ManifestTypes.String, "Substring receiver");
        var (startIndex, length) = SubstringArguments(invocation, lowerExpression);
        RequireType(startIndex, DotBoxdGenerationNames.ManifestTypes.Int, "Substring startIndex");
        RequireType(length, DotBoxdGenerationNames.ManifestTypes.Int, "Substring length");
        return new DotBoxdExpressionModel(
            $"{DotBoxdGenerationNames.Helpers.StringSubstring}({receiver.Source}, {startIndex.Source}, {length.Source})",
            DotBoxdGenerationNames.ManifestTypes.String,
            true);
    }

    private static (DotBoxdExpressionModel StartIndex, DotBoxdExpressionModel Length) SubstringArguments(
        InvocationExpressionSyntax invocation,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
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

    private static string EqualsHelper(DotBoxdExpressionModel receiver)
        => string.Equals(receiver.Type, DotBoxdGenerationNames.ManifestTypes.String, StringComparison.Ordinal)
            ? DotBoxdGenerationNames.Helpers.StringEquals
            : DotBoxdGenerationNames.Helpers.Eq;

    private static void RequireType(DotBoxdExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"{context} must lower to {expected}.");
        }
    }
}
