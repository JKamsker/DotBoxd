using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class KernelMethodArgumentReuseValidator
{
    public static void Validate(
        IMethodSymbol method,
        ExpressionSyntax body,
        SemanticModel bodySemanticModel,
        BoundKernelMethodCall call,
        CancellationToken cancellationToken)
    {
        var usageCounts = ParameterUsageCounts(method, body, bodySemanticModel, cancellationToken);
        foreach (var argument in call.Arguments)
        {
            if (usageCounts.TryGetValue(argument.Parameter, out var count) &&
                count > 1 &&
                argument.Expression is { } expression &&
                !IsRepeatableArgument(expression))
            {
                throw new NotSupportedException(
                    $"[KernelMethod] '{method.Name}' parameter '{argument.Parameter.Name}' is used more than once; " +
                    "pass a repeatable value or store the expensive argument before calling the kernel method.");
            }
        }
    }

    private static Dictionary<IParameterSymbol, int> ParameterUsageCounts(
        IMethodSymbol method,
        ExpressionSyntax body,
        SemanticModel bodySemanticModel,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<IParameterSymbol, int>(SymbolEqualityComparer.Default);
        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bodySemanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not IParameterSymbol parameter ||
                !method.Parameters.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)))
            {
                continue;
            }

            counts[parameter] = counts.TryGetValue(parameter, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static bool IsRepeatableArgument(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsRepeatableArgument(parenthesized.Expression),
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax member => IsRepeatableArgument(member.Expression),
            PrefixUnaryExpressionSyntax unary => IsRepeatableArgument(unary.Operand),
            BinaryExpressionSyntax binary => IsRepeatableArgument(binary.Left) && IsRepeatableArgument(binary.Right),
            _ when expression.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            _ => false
        };
}
