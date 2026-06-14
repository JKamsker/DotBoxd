namespace SafeIR.Interpreter.Internal;

using SafeIR;
using SafeIR.Runtime;

internal static class StringLengthForLoopRunner
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
            !TryCreatePlan(statement, frame, context.Bindings, out var plan))
        {
            return false;
        }

        var iterations = (long)end - start;
        if (!context.CanBulkChargeBindingCalls(plan.Binding, iterations))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, plan.FuelPerIteration);
        context.ChargeBindingCalls(plan.Binding, iterations);

        var length = frame.ReadStringLengthSlot(plan.SourceSlot);
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            var value = plan.AddToTarget
                ? SandboxInt32Math.Add(frame.ReadRawInt32Slot(plan.TargetSlot), length)
                : length;
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
        BindingRegistry bindings,
        out LoopPlan plan)
    {
        plan = default;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not AssignmentStatement assignment)
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsInt32Slot(targetSlot) ||
            !TryGetStringLengthBinding(bindings, out var binding))
        {
            return false;
        }

        if (TryGetStringLengthCall(assignment.Value, frame, out var sourceSlot))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, binding, AddToTarget: false, FuelPerIteration: LoopFuel + 2);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingLength(add, assignment.Name, frame, out sourceSlot))
        {
            plan = new LoopPlan(targetSlot, sourceSlot, binding, AddToTarget: true, FuelPerIteration: LoopFuel + 4);
            return true;
        }

        return false;
    }

    private static bool TryGetAccumulatingLength(
        BinaryExpression expression,
        string targetName,
        InterpreterFrame frame,
        out int sourceSlot)
    {
        sourceSlot = 0;
        return (IsTargetVariable(expression.Left, targetName) &&
                TryGetStringLengthCall(expression.Right, frame, out sourceSlot)) ||
               (IsTargetVariable(expression.Right, targetName) &&
                TryGetStringLengthCall(expression.Left, frame, out sourceSlot));
    }

    private static bool TryGetStringLengthCall(
        Expression expression,
        InterpreterFrame frame,
        out int sourceSlot)
    {
        sourceSlot = 0;
        return expression is CallExpression { Name: "string.length", Arguments.Count: 1 } call &&
               call.Arguments[0] is VariableExpression source &&
               frame.TryGetStringSlot(source.Name, out sourceSlot);
    }

    private static bool IsTargetVariable(Expression expression, string targetName)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, targetName, StringComparison.Ordinal);

    private static bool TryGetStringLengthBinding(BindingRegistry bindings, out BindingDescriptor descriptor)
    {
        if (bindings.TryGet("string.length", out var binding) &&
            binding.Compiled is { Kind: "RuntimeStub" } &&
            binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
            binding.Compiled.Method == nameof(CompiledRuntime.StringLength) &&
            binding.Parameters.Count == 1 &&
            binding.Parameters[0].Equals(SandboxType.String) &&
            binding.ReturnType.Equals(SandboxType.I32) &&
            binding.RequiredCapability is null &&
            binding.Safety == BindingSafety.PureIntrinsic &&
            binding.AuditLevel == AuditLevel.None &&
            binding.CostModel.MaxCallsPerRun is null &&
            (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None)
        {
            descriptor = bindings.GetDescriptor("string.length");
            return true;
        }

        descriptor = null!;
        return false;
    }

    private readonly record struct LoopPlan(
        int TargetSlot,
        int SourceSlot,
        BindingDescriptor Binding,
        bool AddToTarget,
        long FuelPerIteration);
}
