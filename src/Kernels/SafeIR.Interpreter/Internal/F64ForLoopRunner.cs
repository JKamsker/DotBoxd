namespace SafeIR.Interpreter.Internal;

using SafeIR;

internal static class F64ForLoopRunner
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
            !TryCreateBodyPlan(statement, frame, context.Bindings, out var body, out var fuelPerIteration, out var binding))
        {
            return false;
        }

        var iterations = (long)end - start;
        var bindingCalls = BindingCalls(iterations, body.Expression.BindingCallCount);
        if (bindingCalls < 0 ||
            !context.CanBulkChargeLoopIterations(iterations, fuelPerIteration) ||
            !context.CanBulkChargeBindingCalls(binding, bindingCalls))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, fuelPerIteration);
        context.ChargeBindingCalls(binding, bindingCalls);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            frame.WriteRawDoubleSlot(body.TargetSlot, body.Expression.Evaluate(frame));
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
        IBindingCatalog bindings,
        out AssignmentPlan body,
        out long fuelPerIteration,
        out BindingDescriptor binding)
    {
        body = default;
        fuelPerIteration = LoopFuel;
        binding = null!;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not AssignmentStatement assignment ||
            !F64ExpressionPlan.TryCreate(assignment.Value, frame, assignment.Name, bindings, out var expression, out binding))
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsF64Slot(targetSlot) || binding is null)
        {
            return false;
        }

        body = new AssignmentPlan(targetSlot, expression);
        fuelPerIteration += 1 + expression.FuelCost;
        return true;
    }

    private static long BindingCalls(long iterations, int callsPerIteration)
    {
        try
        {
            return checked(iterations * callsPerIteration);
        }
        catch (OverflowException)
        {
            return -1;
        }
    }

    private readonly record struct AssignmentPlan(int TargetSlot, F64ExpressionPlan Expression);
}
