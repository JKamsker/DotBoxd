namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32RemainderAccumulatorCallForLoopRunner
{
    private const long LoopFuel = 5;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options,
        I32CallEvaluator calls)
    {
        if (options.EnableDebugTrace ||
            start != 0 ||
            start >= end ||
            !TryCreatePlan(statement, frame, calls, out var plan))
        {
            return false;
        }

        var iterations = end - start;
        context.RequireAdditionalCallDepth(plan.Call.MaxInlineCallDepth);
        context.ChargeLoopIterations(iterations, LoopFuel + 1 + plan.Call.ExpressionFuelCost);

        var value = SandboxInt32Math.AddRemainderCycleFromZero(
            frame.ReadRawInt32Slot(plan.TargetSlot),
            iterations,
            plan.Call.Divisor);
        frame.WriteRawInt32Slot(plan.TargetSlot, value);
        frame.WriteRawInt32Slot(frame.GetSlot(statement.LocalName), end - 1);
        context.Checkpoint();
        return true;
    }

    private static bool TryCreatePlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        out LoopPlan plan)
    {
        plan = default;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not AssignmentStatement assignment ||
            assignment.Value is not CallExpression call)
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsInt32Slot(targetSlot) ||
            !calls.TryCreateRemainderAccumulatorCallPlan(call, assignment.Name, statement.LocalName, out var callPlan))
        {
            return false;
        }

        plan = new LoopPlan(targetSlot, callPlan);
        return true;
    }

    private readonly record struct LoopPlan(int TargetSlot, I32RemainderAccumulatorCallPlan Call);
}
