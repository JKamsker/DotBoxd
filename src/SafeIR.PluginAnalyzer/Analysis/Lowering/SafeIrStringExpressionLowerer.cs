namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrStringExpressionLowerer
{
    private const string LengthPropertyName = "Length";

    public static SafeIrExpressionModel? TryLowerMember(
        MemberAccessExpressionSyntax member,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(member.Name.Identifier.ValueText, LengthPropertyName, StringComparison.Ordinal))
        {
            return null;
        }

        var receiver = lowerExpression(member.Expression);
        if (!string.Equals(receiver.Type, SafeIrGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            throw new NotSupportedException("String Length receiver must lower to string.");
        }

        return new SafeIrExpressionModel(
            $"{SafeIrGenerationNames.Helpers.StringLength}({receiver.Source})",
            SafeIrGenerationNames.ManifestTypes.Int,
            receiver.Allocates);
    }
}
