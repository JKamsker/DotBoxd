namespace DotBoxd.Kernels.Interpreter.Internal;

using DotBoxd.Kernels;

internal static class ListCountForLoopRunner
{
    private const long LoopFuel = 5;

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
        var count = frame.ReadListCountSlot(plan.SourceSlot);
        var readFuel = SandboxCollectionFuel.Read(count);
        if (!context.CanBulkChargeLoopIterations(iterations, plan.LoopFuelPerIteration) ||
            !context.CanBulkChargeFuel(readFuel, iterations))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, plan.LoopFuelPerIteration);
        context.ChargeBulkFuel(readFuel, iterations);

        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            var value = plan.AddToTarget
                ? SandboxInt32Math.Add(frame.ReadRawInt32Slot(plan.TargetSlot), count)
                : count;
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

        if (TryGetListCountCall(assignment.Value, frame, out var sourceSlot))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, AddToTarget: false, LoopFuelPerIteration: LoopFuel + 2);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingCount(add, assignment.Name, frame, out sourceSlot))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, AddToTarget: true, LoopFuelPerIteration: LoopFuel + 4);
            return true;
        }

        return false;
    }

    private static bool TryGetAccumulatingCount(
        BinaryExpression expression,
        string targetName,
        InterpreterFrame frame,
        out int sourceSlot)
    {
        sourceSlot = 0;
        return (IsTargetVariable(expression.Left, targetName) &&
                TryGetListCountCall(expression.Right, frame, out sourceSlot)) ||
               (IsTargetVariable(expression.Right, targetName) &&
                TryGetListCountCall(expression.Left, frame, out sourceSlot));
    }

    private static bool TryGetListCountCall(
        Expression expression,
        InterpreterFrame frame,
        out int sourceSlot)
    {
        sourceSlot = 0;
        return expression is CallExpression { Name: "list.count", Arguments.Count: 1 } call &&
               call.Arguments[0] is VariableExpression source &&
               frame.TryGetListSlot(source.Name, out sourceSlot);
    }

    private static bool IsTargetVariable(Expression expression, string targetName)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, targetName, StringComparison.Ordinal);

    private readonly record struct LoopPlan(
        int TargetSlot,
        int SourceSlot,
        bool AddToTarget,
        long LoopFuelPerIteration);
}
