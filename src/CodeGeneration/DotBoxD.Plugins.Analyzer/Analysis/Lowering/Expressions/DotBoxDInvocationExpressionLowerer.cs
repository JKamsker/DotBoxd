using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDInvocationExpressionLowerer
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
    private const string SubstringArgumentCountMessage =
        "Substring calls must have startIndex and length arguments.";

    public static DotBoxDExpressionModel Lower(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method &&
            HasLocalAttribute(method))
        {
            throw new NotSupportedException("[Local] context members cannot be used in lowered server-side IR.");
        }

        if (DotBoxDHostBindingExpressionLowerer.TryLower(invocation, context, lowerExpression) is { } hostCall)
        {
            return hostCall;
        }

        if (DotBoxDResultBuilderExpressionLowerer.TryLower(invocation, context, lowerExpression) is { } resultBuilder)
        {
            return resultBuilder;
        }

        if (DotBoxDKernelMethodInliner.TryInline(invocation, context, lowerExpression) is { } inlined)
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

    private static bool HasLocalAttribute(IMethodSymbol method)
        => method.GetAttributes().Any(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            DotBoxDMetadataNames.LocalAttribute,
            StringComparison.Ordinal));

    private static DotBoxDExpressionModel LowerEquals(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != EqualsArgumentCount)
        {
            throw new NotSupportedException(EqualsArgumentCountMessage);
        }

        var receiver = lowerExpression(member.Expression);
        var value = lowerExpression(arguments[EqualsValueArgumentIndex].Expression);

        // Reuse the `==` lowering so instance Equals enforces the SAME scalar-only guard: a list/map/record/array
        // operand compares by STRUCTURE in the sandbox but by REFERENCE in C#, so it must fail safe rather than
        // silently lower to a structural comparison. Scalars (and string Equals) lower exactly as `==` does.
        return DotBoxDEqualityExpressionLowerer.Lower(
            receiver,
            value,
            negate: false,
            receiver.Allocates || value.Allocates);
    }

    private static DotBoxDExpressionModel LowerSubstring(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var receiver = lowerExpression(member.Expression);
        RequireType(receiver, DotBoxDGenerationNames.ManifestTypes.String, "Substring receiver");
        var (startIndex, length) = SubstringArguments(invocation, lowerExpression);
        RequireType(startIndex, DotBoxDGenerationNames.ManifestTypes.Int, "Substring startIndex");
        RequireType(length, DotBoxDGenerationNames.ManifestTypes.Int, "Substring length");
        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.StringSubstring}({receiver.Source}, {startIndex.Source}, {length.Source})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);
    }

    private static (DotBoxDExpressionModel StartIndex, DotBoxDExpressionModel Length) SubstringArguments(
        InvocationExpressionSyntax invocation,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
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

    private static void RequireType(DotBoxDExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"{context} must lower to {expected}.");
        }
    }
}
