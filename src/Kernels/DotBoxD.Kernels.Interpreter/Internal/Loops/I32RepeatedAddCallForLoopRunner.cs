namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32RepeatedAddCallForLoopRunner
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
            start >= end ||
            !TryCreatePlan(statement, frame, calls, out var plan))
        {
            return false;
        }

        var iterations = (long)end - start;
        context.RequireAdditionalCallDepth(plan.Call.MaxInlineCallDepth);
        context.ChargeLoopIterations(iterations, LoopFuel + 1 + plan.Call.ExpressionFuelCost);

        var value = SandboxInt32Math.AddRepeated(
            frame.ReadRawInt32Slot(plan.TargetSlot),
            plan.Call.Delta,
            iterations);
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
            !calls.TryCreateRepeatedAddCallPlan(call, assignment.Name, out var callPlan))
        {
            return false;
        }

        plan = new LoopPlan(targetSlot, callPlan);
        return true;
    }

    private readonly record struct LoopPlan(int TargetSlot, I32RepeatedAddCallPlan Call);
}
