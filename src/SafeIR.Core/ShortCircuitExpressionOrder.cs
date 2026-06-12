namespace SafeIR;

public readonly record struct ShortCircuitOperands(Expression First, Expression Second, bool Reordered);

public static class ShortCircuitExpressionOrder
{
    public static ShortCircuitOperands Choose(
        BinaryExpression expression,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        if (expression.Operator is not ("&&" or "||"))
        {
            return new ShortCircuitOperands(expression.Left, expression.Right, Reordered: false);
        }

        var left = Estimate(expression.Left, bindings, functions);
        var right = Estimate(expression.Right, bindings, functions);
        if (left.CanReorder && right.CanReorder && right.Cost < left.Cost)
        {
            return new ShortCircuitOperands(expression.Right, expression.Left, Reordered: true);
        }

        return new ShortCircuitOperands(expression.Left, expression.Right, Reordered: false);
    }

    private static CostEstimate Estimate(
        Expression expression,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
        => expression switch
        {
            LiteralExpression => CostEstimate.Pure(0),
            VariableExpression => CostEstimate.Pure(1),
            UnaryExpression unary => Estimate(unary.Operand, bindings, functions).Add(1),
            BinaryExpression binary => Estimate(binary.Left, bindings, functions)
                .Combine(Estimate(binary.Right, bindings, functions))
                .Add(2),
            CallExpression call => EstimateCall(call, bindings, functions),
            _ => CostEstimate.NotReorderable(long.MaxValue / 4)
        };

    private static CostEstimate EstimateCall(
        CallExpression call,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        var estimate = CostEstimate.Pure(8);
        foreach (var argument in call.Arguments)
        {
            estimate = estimate.Combine(Estimate(argument, bindings, functions));
        }

        if (functions.TryGetValue(call.Name, out var function))
        {
            return estimate.Combine(new CostEstimate(20, IsPure(function.Effects)));
        }

        if (bindings.TryGet(call.Name, out var binding))
        {
            return estimate.Combine(new CostEstimate(
                Math.Max(0, binding.CostModel.BaseFuel),
                IsPure(binding.Effects)));
        }

        if (SandboxCollectionFuel.IsCollectionIntrinsic(call.Name))
        {
            return estimate.Combine(CostEstimate.Pure(SandboxCollectionFuel.EstimateCall(call.Name, call.Arguments.Count)));
        }

        return estimate.Combine(CostEstimate.NotReorderable(long.MaxValue / 4));
    }

    private static bool IsPure(SandboxEffect effects) => (effects & ~SandboxEffects.Pure) == SandboxEffect.None;

    private readonly record struct CostEstimate(long Cost, bool CanReorder)
    {
        public static CostEstimate Pure(long cost) => new(cost, CanReorder: true);

        public static CostEstimate NotReorderable(long cost) => new(cost, CanReorder: false);

        public CostEstimate Add(long cost) => new(SaturatingAdd(Cost, cost), CanReorder);

        public CostEstimate Combine(CostEstimate other)
            => new(SaturatingAdd(Cost, other.Cost), CanReorder && other.CanReorder);

        private static long SaturatingAdd(long left, long right)
            => long.MaxValue - left < right ? long.MaxValue : left + right;
    }
}
