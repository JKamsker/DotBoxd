namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

// Fast path for `forRange { <i64 assigns> }` (i32 loop var, i64 assignment targets). Evaluates the body with
// unboxed i64 plans, avoiding the boxed evaluator's per-op I64Value allocation. Bulk-charges loop fuel per
// iteration, matching the i32/f64 runners (loop base 5 + per assignment 1 + expression node fuel).
internal static class I64ForLoopRunner
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
            !TryCreateBody(statement, frame, out var body, out var fuelPerIteration))
        {
            return false;
        }

        var iterations = (long)end - start;
        if (!context.CanBulkChargeLoopIterations(iterations, fuelPerIteration))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, fuelPerIteration);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            for (var statementIndex = 0; statementIndex < body.Length; statementIndex++)
            {
                var assignment = body[statementIndex];
                frame.WriteRawInt64Slot(assignment.TargetSlot, assignment.Expression.Evaluate(frame));
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }

        return true;
    }

    private static bool TryCreateBody(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out AssignmentPlan[] body,
        out long fuelPerIteration)
    {
        body = [];
        fuelPerIteration = LoopFuel;
        if (statement.Body.Count == 0)
        {
            return false;
        }

        var plans = new AssignmentPlan[statement.Body.Count];
        var assignedSlots = new HashSet<int>();
        var fuel = LoopFuel;
        for (var i = 0; i < statement.Body.Count; i++)
        {
            if (statement.Body[i] is not AssignmentStatement assignment ||
                !I64ExpressionPlan.TryCreate(assignment.Value, frame, CanReadSlot, out var expression))
            {
                return false;
            }

            var targetSlot = frame.GetSlot(assignment.Name);
            if (!frame.IsI64Slot(targetSlot))
            {
                return false;
            }

            plans[i] = new AssignmentPlan(targetSlot, expression);
            assignedSlots.Add(targetSlot);
            fuel += 1 + expression.FuelCost;
        }

        body = plans;
        fuelPerIteration = fuel;
        return true;

        bool CanReadSlot(int slot) => frame.IsSlotAssigned(slot) || assignedSlots.Contains(slot);
    }

    private readonly record struct AssignmentPlan(int TargetSlot, I64ExpressionPlan Expression);
}
