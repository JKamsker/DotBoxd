namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32ModuloBranchAccumulatorForLoopRunner
{
    private const long LoopFuel = 5;
    private const int IfStatementFuel = 1;
    private const int AssignmentFuel = 1;
    private const int BranchExpressionFuel = 3;
    private const int ModuloEqualsConditionFuel = 5;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
    {
        if (options.EnableDebugTrace ||
            start != 0 ||
            start >= end ||
            !TryCreatePlan(statement, frame, out var plan))
        {
            return false;
        }

        var iterations = end - start;
        context.ChargeLoopIterations(iterations, plan.FuelPerIteration);

        var value = SandboxInt32Math.AddModuloBranchDeltasFromZero(
            frame.ReadRawInt32Slot(plan.TargetSlot),
            iterations,
            plan.Divisor,
            plan.MatchRemainder,
            plan.ThenDelta,
            plan.ElseDelta);
        frame.WriteRawInt32Slot(plan.TargetSlot, value);
        frame.WriteRawInt32Slot(frame.GetSlot(statement.LocalName), end - 1);
        context.Checkpoint();
        return true;
    }

    private static bool TryCreatePlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        out LoopPlan plan)
    {
        plan = default;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not IfStatement branch ||
            !TryGetModuloEquals(branch.Condition, statement.LocalName, out var divisor, out var matchRemainder) ||
            !TryGetBranchAssignment(branch.Then, frame, out var target, out var thenDelta) ||
            !TryGetBranchAssignment(branch.Else, frame, out var elseTarget, out var elseDelta) ||
            !string.Equals(target, elseTarget, StringComparison.Ordinal) ||
            !DeltasHaveSameDirection(thenDelta, elseDelta))
        {
            return false;
        }

        var targetSlot = frame.GetSlot(target);
        if (!frame.IsInt32Slot(targetSlot))
        {
            return false;
        }

        plan = new LoopPlan(
            targetSlot,
            divisor,
            matchRemainder,
            thenDelta,
            elseDelta,
            LoopFuel + IfStatementFuel + ModuloEqualsConditionFuel + AssignmentFuel + BranchExpressionFuel);
        return true;
    }

    private static bool TryGetModuloEquals(
        Expression expression,
        string loopLocal,
        out int divisor,
        out int matchRemainder)
    {
        divisor = 0;
        matchRemainder = 0;
        if (expression is not BinaryExpression { Operator: "==" } equals)
        {
            return false;
        }

        return (TryGetLoopModulo(equals.Left, loopLocal, out divisor) &&
                TryReadI32(equals.Right, out matchRemainder)) ||
               (TryGetLoopModulo(equals.Right, loopLocal, out divisor) &&
                TryReadI32(equals.Left, out matchRemainder));
    }

    private static bool TryGetLoopModulo(Expression expression, string loopLocal, out int divisor)
    {
        divisor = 0;
        if (expression is BinaryExpression
            {
                Operator: "%",
                Left: VariableExpression variable,
                Right: LiteralExpression { Value: I32Value value }
            } &&
            value.Value > 0 &&
            string.Equals(variable.Name, loopLocal, StringComparison.Ordinal))
        {
            divisor = value.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetBranchAssignment(
        IReadOnlyList<Statement> branch,
        InterpreterFrame frame,
        out string target,
        out int delta)
    {
        target = "";
        delta = 0;
        if (branch.Count != 1 ||
            branch[0] is not AssignmentStatement assignment ||
            !frame.IsInt32Local(assignment.Name) ||
            !TryGetDelta(assignment.Value, assignment.Name, out delta))
        {
            return false;
        }

        target = assignment.Name;
        return true;
    }

    private static bool TryGetDelta(Expression expression, string target, out int delta)
    {
        delta = 0;
        return expression is BinaryExpression { Operator: "+" } add &&
               ((IsTarget(add.Left, target) && TryReadI32(add.Right, out delta)) ||
                (IsTarget(add.Right, target) && TryReadI32(add.Left, out delta)));
    }

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

    private static bool IsTarget(Expression expression, string target)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, target, StringComparison.Ordinal);

    private static bool DeltasHaveSameDirection(int left, int right)
        => left >= 0 && right >= 0 || left <= 0 && right <= 0;

    private readonly record struct LoopPlan(
        int TargetSlot,
        int Divisor,
        int MatchRemainder,
        int ThenDelta,
        int ElseDelta,
        long FuelPerIteration);
}
