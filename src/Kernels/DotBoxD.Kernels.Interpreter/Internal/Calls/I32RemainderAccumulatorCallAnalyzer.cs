namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32RemainderAccumulatorCallAnalyzer
{
    public static bool TryCreate(
        IReadOnlyDictionary<string, SandboxFunction> functions,
        CallExpression call,
        string targetName,
        string loopLocal,
        out I32RemainderAccumulatorCallPlan plan)
    {
        plan = default;
        if (call.Arguments.Count != 2 ||
            !TryGetTargetAndModulo(call, targetName, loopLocal, out var divisor, out var argumentFuel) ||
            !functions.TryGetValue(call.Name, out var function) ||
            !IsTwoArgumentAdd(function, out var bodyFuel))
        {
            return false;
        }

        plan = new I32RemainderAccumulatorCallPlan(
            divisor,
            argumentFuel + bodyFuel + 3,
            MaxInlineCallDepth: 1);
        return true;
    }

    private static bool TryGetTargetAndModulo(
        CallExpression call,
        string targetName,
        string loopLocal,
        out int divisor,
        out int argumentFuel)
    {
        divisor = 0;
        argumentFuel = 0;
        if (IsTarget(call.Arguments[0], targetName) &&
            TryGetLoopModulo(call.Arguments[1], loopLocal, out divisor))
        {
            argumentFuel = I32ExpressionEvaluator.FuelCost(call.Arguments[0]) +
                           I32ExpressionEvaluator.FuelCost(call.Arguments[1]);
            return true;
        }

        if (IsTarget(call.Arguments[1], targetName) &&
            TryGetLoopModulo(call.Arguments[0], loopLocal, out divisor))
        {
            argumentFuel = I32ExpressionEvaluator.FuelCost(call.Arguments[0]) +
                           I32ExpressionEvaluator.FuelCost(call.Arguments[1]);
            return true;
        }

        return false;
    }

    private static bool IsTwoArgumentAdd(SandboxFunction function, out int bodyFuel)
    {
        bodyFuel = 0;
        if (function.Parameters.Count != 2 ||
            function.Parameters.Any(parameter => parameter.Type != SandboxType.I32) ||
            function.ReturnType != SandboxType.I32 ||
            function.Body.Count != 1 ||
            function.Body[0] is not ReturnStatement { Value: BinaryExpression { Operator: "+" } add } ||
            !IsParameterPair(add, function.Parameters[0].Name, function.Parameters[1].Name))
        {
            return false;
        }

        bodyFuel = I32ExpressionEvaluator.FuelCost(add);
        return true;
    }

    private static bool IsTarget(Expression expression, string targetName)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, targetName, StringComparison.Ordinal);

    private static bool TryGetLoopModulo(Expression expression, string loopLocal, out int divisor)
    {
        divisor = 0;
        if (expression is BinaryExpression
            {
                Operator: "%",
                Left: VariableExpression variable,
                Right: LiteralExpression { Value: I32Value value }
            } &&
            value.Value > 0 &&
            string.Equals(variable.Name, loopLocal, StringComparison.Ordinal))
        {
            divisor = value.Value;
            return true;
        }

        return false;
    }

    private static bool IsParameterPair(BinaryExpression add, string leftParameter, string rightParameter)
        => (IsParameter(add.Left, leftParameter) && IsParameter(add.Right, rightParameter)) ||
           (IsParameter(add.Left, rightParameter) && IsParameter(add.Right, leftParameter));

    private static bool IsParameter(Expression expression, string parameter)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, parameter, StringComparison.Ordinal);
}
