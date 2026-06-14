namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32RepeatedAddCallAnalyzer
{
    public static bool TryCreate(
        IReadOnlyDictionary<string, SandboxFunction> functions,
        CallExpression call,
        string targetName,
        out I32RepeatedAddCallPlan plan)
    {
        plan = default;
        if (call.Arguments.Count != 1 ||
            call.Arguments[0] is not VariableExpression argument ||
            !string.Equals(argument.Name, targetName, StringComparison.Ordinal) ||
            !functions.TryGetValue(call.Name, out var function) ||
            !TryGetDelta(function, out var delta, out var expression))
        {
            return false;
        }

        plan = new I32RepeatedAddCallPlan(
            delta,
            I32ExpressionEvaluator.FuelCost(expression) + 4,
            MaxInlineCallDepth: 1);
        return true;
    }

    private static bool TryGetDelta(SandboxFunction function, out int delta, out Expression expression)
    {
        delta = 0;
        expression = null!;
        if (function.Parameters.Count != 1 ||
            function.Parameters[0].Type != SandboxType.I32 ||
            function.ReturnType != SandboxType.I32 ||
            function.Body.Count != 1 ||
            function.Body[0] is not ReturnStatement ret)
        {
            return false;
        }

        var parameter = function.Parameters[0].Name;
        if (ret.Value is VariableExpression variable &&
            string.Equals(variable.Name, parameter, StringComparison.Ordinal))
        {
            expression = ret.Value;
            return true;
        }

        if (TryGetAddDelta(ret.Value, parameter, out delta) ||
            TryGetSubtractDelta(ret.Value, parameter, out delta))
        {
            expression = ret.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetAddDelta(Expression expression, string parameter, out int delta)
    {
        delta = 0;
        return expression is BinaryExpression { Operator: "+" } add &&
               ((IsParameter(add.Left, parameter) && TryReadI32(add.Right, out delta)) ||
                (IsParameter(add.Right, parameter) && TryReadI32(add.Left, out delta)));
    }

    private static bool TryGetSubtractDelta(Expression expression, string parameter, out int delta)
    {
        delta = 0;
        if (expression is not BinaryExpression { Operator: "-", Left: VariableExpression left } subtract ||
            !string.Equals(left.Name, parameter, StringComparison.Ordinal) ||
            !TryReadI32(subtract.Right, out var value) ||
            value == int.MinValue)
        {
            return false;
        }

        delta = -value;
        return true;
    }

    private static bool IsParameter(Expression expression, string parameter)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, parameter, StringComparison.Ordinal);

    private static bool TryReadI32(Expression expression, out int value)
    {
        if (expression is LiteralExpression { Value: I32Value i32 })
        {
            value = i32.Value;
            return true;
        }

        value = 0;
        return false;
    }
}
