namespace DotBoxd.Kernels.Interpreter.Internal;

using DotBoxd.Kernels;

internal static class I32ForLoopRunner
{
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
            !TryCreateBodyPlan(statement, frame, calls, out var body, out var fuelPerIteration))
        {
            return false;
        }

        context.ChargeLoopIterations((long)end - start, fuelPerIteration);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            for (var statementIndex = 0; statementIndex < body.Length; statementIndex++)
            {
                var assignment = body[statementIndex];
                frame.WriteRawInt32Slot(assignment.TargetSlot, assignment.Expression.Evaluate(frame, context));
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }

        return true;
    }

    private static bool TryCreateBodyPlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        out AssignmentPlan[] body,
        out long fuelPerIteration)
    {
        body = new AssignmentPlan[statement.Body.Count];
        fuelPerIteration = 5;
        for (var i = 0; i < statement.Body.Count; i++)
        {
            if (statement.Body[i] is not AssignmentStatement assignment ||
                !I32ExpressionPlan.TryCreate(assignment.Value, frame, statement.LocalName, calls, out var expression))
            {
                body = [];
                return false;
            }

            var targetSlot = frame.GetSlot(assignment.Name);
            if (!frame.IsInt32Slot(targetSlot))
            {
                body = [];
                return false;
            }

            body[i] = new AssignmentPlan(targetSlot, expression);
            fuelPerIteration += 1 + expression.FuelCost;
        }

        return true;
    }

    private readonly record struct AssignmentPlan(int TargetSlot, I32ExpressionPlan Expression);
}
