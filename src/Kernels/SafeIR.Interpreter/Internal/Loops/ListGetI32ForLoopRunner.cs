namespace SafeIR.Interpreter.Internal;

using SafeIR;

internal static class ListGetI32ForLoopRunner
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
        if (!frame.TryReadListInt32ItemsSlot(plan.SourceSlot, out var items) ||
            !context.CanBulkChargeLoopIterations(iterations, plan.LoopFuelPerIteration) ||
            !context.CanBulkChargeFuel(readFuel, iterations))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, plan.LoopFuelPerIteration);
        context.ChargeBulkFuel(readFuel, iterations);

        var hasRemainderIndex = plan.Index.TryGetRawVariableRemainderConstant(out var remainderSlot, out var remainderDivisor);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            var item = ReadItem(items, hasRemainderIndex
                ? SandboxInt32Math.Remainder(frame.ReadRawInt32Slot(remainderSlot), remainderDivisor)
                : plan.Index.Evaluate(frame, context));
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

    private static int ReadItem(int[] items, int index)
    {
        if ((uint)index >= (uint)items.Length)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "list index is out of range"));
        }

        return items[index];
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

        if (TryGetListGetCall(assignment.Value, statement.LocalName, frame, out var sourceSlot, out var index))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, index, AddToTarget: false, LoopFuelPerIteration: LoopFuel + 2 + index.FuelCost);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingGet(add, assignment.Name, statement.LocalName, frame, out sourceSlot, out index))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, index, AddToTarget: true, LoopFuelPerIteration: LoopFuel + 4 + index.FuelCost);
            return true;
        }

        return false;
    }

    private static bool TryGetAccumulatingGet(
        BinaryExpression expression,
        string targetName,
        string loopLocal,
        InterpreterFrame frame,
        out int sourceSlot,
        out I32ExpressionPlan index)
    {
        sourceSlot = 0;
        index = null!;
        return (IsTargetVariable(expression.Left, targetName) &&
                TryGetListGetCall(expression.Right, loopLocal, frame, out sourceSlot, out index)) ||
               (IsTargetVariable(expression.Right, targetName) &&
                TryGetListGetCall(expression.Left, loopLocal, frame, out sourceSlot, out index));
    }

    private static bool TryGetListGetCall(
        Expression expression,
        string loopLocal,
        InterpreterFrame frame,
        out int sourceSlot,
        out I32ExpressionPlan index)
    {
        sourceSlot = 0;
        index = null!;
        return expression is CallExpression { Name: "list.get", Arguments.Count: 2 } call &&
               call.Arguments[0] is VariableExpression source &&
               frame.TryGetListSlot(source.Name, out sourceSlot) &&
               I32ExpressionPlan.TryCreate(call.Arguments[1], frame, loopLocal, out index);
    }

    private static bool IsTargetVariable(Expression expression, string targetName)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, targetName, StringComparison.Ordinal);

    private readonly record struct LoopPlan(
        int TargetSlot,
        int SourceSlot,
        I32ExpressionPlan Index,
        bool AddToTarget,
        long LoopFuelPerIteration);
}
