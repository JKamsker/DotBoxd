namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

// Fast path for `forRange { if (<i32 comparison>) { <f64 assigns> } else { <f64 assigns> } }` — an i32 loop
// variable with an f64 (binding-free) branched body. Evaluates the condition and both branches with unboxed
// i32/f64 plans, avoiding the boxed statement executor's per-op allocation. Metering matches the general path:
// per iteration 5 (loop) + 1 + condition-node-fuel (if), and each taken assignment 1 + f64 expression node fuel.
internal static class BranchedF64ForLoopRunner
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
            statement.Body.Count != 1 ||
            statement.Body[0] is not IfStatement branch ||
            !I32ComparisonPlan.TryCreate(branch.Condition, frame, statement.LocalName, calls, out var condition) ||
            !TryCreateBranch(branch.Then, frame, context.Bindings, out var thenBranch) ||
            !TryCreateBranch(branch.Else, frame, context.Bindings, out var elseBranch))
        {
            return false;
        }

        var loopSlot = frame.GetSlot(statement.LocalName);
        long conditionFuel = 1 + condition.FuelCost;
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            context.ChargeLoopIteration(LoopFuel);
            context.ChargeFuel(conditionFuel);
            frame.WriteRawInt32Slot(loopSlot, i);
            var taken = condition.Evaluate(frame, context) ? thenBranch : elseBranch;
            context.ChargeFuel(taken.Fuel);
            var assignments = taken.Assignments;
            for (var statementIndex = 0; statementIndex < assignments.Length; statementIndex++)
            {
                var assignment = assignments[statementIndex];
                frame.WriteRawDoubleSlot(assignment.TargetSlot, assignment.Expression.Evaluate(frame));
            }

            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }

        return true;
    }

    private static bool TryCreateBranch(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        IBindingCatalog bindings,
        out Branch branch)
    {
        branch = default;
        var assignments = new AssignmentPlan[statements.Count];
        long fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not AssignmentStatement assignment ||
                !F64ExpressionPlan.TryCreate(assignment.Value, frame, assignment.Name, bindings, out var expression, out var binding) ||
                binding is not null)
            {
                return false;
            }

            var targetSlot = frame.GetSlot(assignment.Name);
            if (!frame.IsF64Slot(targetSlot))
            {
                return false;
            }

            assignments[i] = new AssignmentPlan(targetSlot, expression);
            fuel += 1 + expression.FuelCost;
        }

        branch = new Branch(assignments, fuel);
        return true;
    }

    private readonly record struct AssignmentPlan(int TargetSlot, F64ExpressionPlan Expression);

    private readonly record struct Branch(AssignmentPlan[] Assignments, long Fuel);
}
