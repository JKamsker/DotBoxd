namespace SafeIR.Interpreter.Internal;

using SafeIR;

internal static class MapGetI32ForLoopRunner
{
    private const long LoopFuel = 5;
    private const int LiteralFuel = 1;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
    {
        if (options.EnableDebugTrace ||
            start >= end ||
            !TryCreatePlan(statement, frame, out var plan))
        {
            return false;
        }

        var iterations = (long)end - start;
        var count = frame.ReadMapCountSlot(plan.SourceSlot);
        var readFuel = SandboxCollectionFuel.Read(count);
        if (!context.CanBulkChargeLoopIterations(iterations, plan.LoopFuelPerIteration) ||
            !context.CanBulkChargeFuel(readFuel, iterations) ||
            !context.CanBulkChargeValue(plan.Key, iterations))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, plan.LoopFuelPerIteration);
        context.ChargeBulkFuel(readFuel, iterations);
        context.ChargeBulkValue(plan.Key, iterations);

        var item = frame.ReadMapInt32ValueSlot(plan.SourceSlot, plan.Key);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            var value = plan.AddToTarget
                ? SandboxInt32Math.Add(frame.ReadRawInt32Slot(plan.TargetSlot), item)
                : item;
            frame.WriteRawInt32Slot(plan.TargetSlot, value);

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }

        return true;
    }

    private static bool TryCreatePlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out LoopPlan plan)
    {
        plan = default;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not AssignmentStatement assignment)
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsInt32Slot(targetSlot))
        {
            return false;
        }

        if (TryGetMapGetCall(assignment.Value, frame, out var sourceSlot, out var key))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, key, AddToTarget: false, LoopFuelPerIteration: LoopFuel + 2 + LiteralFuel);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingGet(add, assignment.Name, frame, out sourceSlot, out key))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, key, AddToTarget: true, LoopFuelPerIteration: LoopFuel + 4 + LiteralFuel);
            return true;
        }

        return false;
    }

    private static bool TryGetAccumulatingGet(
        BinaryExpression expression,
        string targetName,
        InterpreterFrame frame,
        out int sourceSlot,
        out SandboxValue key)
    {
        sourceSlot = 0;
        key = null!;
        return (IsTargetVariable(expression.Left, targetName) &&
                TryGetMapGetCall(expression.Right, frame, out sourceSlot, out key)) ||
               (IsTargetVariable(expression.Right, targetName) &&
                TryGetMapGetCall(expression.Left, frame, out sourceSlot, out key));
    }

    private static bool TryGetMapGetCall(
        Expression expression,
        InterpreterFrame frame,
        out int sourceSlot,
        out SandboxValue key)
    {
        sourceSlot = 0;
        key = null!;
        if (expression is not CallExpression { Name: "map.get", Arguments.Count: 2 } call ||
            call.Arguments[0] is not VariableExpression source ||
            call.Arguments[1] is not LiteralExpression literal ||
            !frame.TryGetMapSlot(source.Name, out sourceSlot))
        {
            return false;
        }

        key = literal.Value;
        return true;
    }

    private static bool IsTargetVariable(Expression expression, string targetName)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, targetName, StringComparison.Ordinal);

    private readonly record struct LoopPlan(
        int TargetSlot,
        int SourceSlot,
        SandboxValue Key,
        bool AddToTarget,
        long LoopFuelPerIteration);
}
