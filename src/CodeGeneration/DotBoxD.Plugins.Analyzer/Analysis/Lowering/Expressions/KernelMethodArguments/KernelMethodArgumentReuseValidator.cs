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
        var firstUseOrder = ParameterFirstUseOrder(method, body, bodySemanticModel, cancellationToken);
        Validate(method, usageCounts, firstUseOrder, call);
    }

    public static void Validate(
        IMethodSymbol method,
        IReadOnlyDictionary<IParameterSymbol, int> usageCounts,
        IReadOnlyList<IParameterSymbol> firstUseOrder,
        BoundKernelMethodCall call)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Expression is not { } expression || IsRepeatableArgument(expression))
            {
                continue;
            }

            if (!usageCounts.TryGetValue(argument.Parameter, out var count) || count == 0)
            {
                throw new KernelMethodArgumentReuseException(
                    $"[KernelMethod] '{method.Name}' parameter '{argument.Parameter.Name}' is not used; " +
                    "non-repeatable arguments must be evaluated exactly once.",
                    PluginDiagnosticLocation.From(expression.GetLocation()));
            }

            if (count > 1)
            {
                throw new KernelMethodArgumentReuseException(
                    $"[KernelMethod] '{method.Name}' parameter '{argument.Parameter.Name}' is used more than once; " +
                    "pass a repeatable value or store the expensive argument before calling the kernel method.",
                    PluginDiagnosticLocation.From(expression.GetLocation()));
            }
        }

        ValidateNonRepeatableEvaluationOrder(method, usageCounts, firstUseOrder, call);
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
            if (IsInsideNameof(identifier))
            {
                continue;
            }

            if (bodySemanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not IParameterSymbol parameter ||
                !method.Parameters.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)))
            {
                continue;
            }

            counts[parameter] = counts.TryGetValue(parameter, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static List<IParameterSymbol> ParameterFirstUseOrder(
        IMethodSymbol method,
        ExpressionSyntax body,
        SemanticModel bodySemanticModel,
        CancellationToken cancellationToken)
    {
        var order = new List<IParameterSymbol>();
        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsInsideNameof(identifier))
            {
                continue;
            }

            if (bodySemanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not IParameterSymbol parameter ||
                !method.Parameters.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)) ||
                order.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)))
            {
                continue;
            }

            order.Add(parameter);
        }

        return order;
    }

    private static void ValidateNonRepeatableEvaluationOrder(
        IMethodSymbol method,
        IReadOnlyDictionary<IParameterSymbol, int> usageCounts,
        IReadOnlyList<IParameterSymbol> firstUseOrder,
        BoundKernelMethodCall call)
    {
        var expected = call.EvaluationOrder
            .Where(argument => argument.Expression is { } expression &&
                               !IsRepeatableArgument(expression) &&
                               usageCounts.TryGetValue(argument.Parameter, out var count) &&
                               count > 0)
            .Select(static argument => argument.Parameter)
            .ToArray();
        if (expected.Length <= 1)
        {
            return;
        }

        var actual = firstUseOrder
            .Where(parameter => expected.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)))
            .ToArray();
        if (SameOrder(expected, actual))
        {
            return;
        }

        var first = call.EvaluationOrder.First(argument =>
            argument.Expression is { } expression &&
            !IsRepeatableArgument(expression) &&
            expected.Any(parameter => SymbolEqualityComparer.Default.Equals(parameter, argument.Parameter)));
        throw new KernelMethodArgumentReuseException(
            $"[KernelMethod] '{method.Name}' non-repeatable arguments must be used in call-site evaluation order.",
            PluginDiagnosticLocation.From(first.Expression!.GetLocation()));
    }

    private static bool SameOrder(IReadOnlyList<IParameterSymbol> expected, IReadOnlyList<IParameterSymbol> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(expected[i], actual[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsRepeatableArgument(ExpressionSyntax expression)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsRepeatableArgument(parenthesized.Expression),
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax member => IsRepeatableArgument(member.Expression),
            PrefixUnaryExpressionSyntax unary => IsRepeatableArgument(unary.Operand),
            BinaryExpressionSyntax binary => IsRepeatableArgument(binary.Left) && IsRepeatableArgument(binary.Right),
            _ when expression.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            DefaultExpressionSyntax => true,
            _ => false
        };

    private static bool IsInsideNameof(IdentifierNameSyntax identifier)
        => identifier.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
            invocation.ArgumentList.Span.Contains(identifier.SpanStart));
}
