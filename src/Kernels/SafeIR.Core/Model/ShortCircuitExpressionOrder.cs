namespace SafeIR;

public readonly record struct ShortCircuitOperands(Expression First, Expression Second, bool Reordered);

public static class ShortCircuitExpressionOrder
{
    public static ShortCircuitOperands Choose(
        BinaryExpression expression,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
        => new(expression.Left, expression.Right, Reordered: false);
}
