namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdStringExpressionLowerer
{
    private const string LengthPropertyName = "Length";

    public static DotBoxdExpressionModel? TryLowerMember(
        MemberAccessExpressionSyntax member,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(member.Name.Identifier.ValueText, LengthPropertyName, StringComparison.Ordinal))
        {
            return null;
        }

        var receiver = lowerExpression(member.Expression);
        if (!string.Equals(receiver.Type, DotBoxdGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            throw new NotSupportedException("String Length receiver must lower to string.");
        }

        return new DotBoxdExpressionModel(
            $"{DotBoxdGenerationNames.Helpers.StringLength}({receiver.Source})",
            DotBoxdGenerationNames.ManifestTypes.Int,
            receiver.Allocates);
    }
}
