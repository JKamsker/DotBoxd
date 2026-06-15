namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

// Fast path for `while (<i32 comparison>) { <i32 assignments> }`. Evaluates the condition and body with
// unboxed i32 plans instead of the boxed statement executor (which allocates a value per op).
//
// Metering matches the boxed while path exactly: the while statement's own Fuel(1) was already charged by
// ExecuteStatementAsync before dispatch; here each condition evaluation (N body iterations + 1 exit check)
// charges the condition's node fuel, and each executed iteration additionally charges 5 (ChargeLoopIteration)
// plus, per body assignment, 1 (the set statement) + the expression's node fuel.
internal static class WhileI32ForLoopRunner
{
    private const long LoopFuel = 5;

    public static bool TryRun(
        WhileStatement statement,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options,
        I32CallEvaluator calls)
    {
        if (options.EnableDebugTrace ||
            !I32ComparisonPlan.TryCreate(statement.Condition, frame, "", calls, out var condition) ||
            !TryCreateBody(statement.Body, frame, calls, out var body, out var bodyFuel))
        {
            return false;
        }

        long conditionFuel = condition.FuelCost;
        var checkpoint = 4096;
        while (true)
        {
            context.ChargeFuel(conditionFuel);
            if (!condition.Evaluate(frame, context))
            {
                break;
            }

            context.ChargeLoopIteration(LoopFuel);
            context.ChargeFuel(bodyFuel);
            for (var i = 0; i < body.Length; i++)
            {
                var assignment = body[i];
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

    private static bool TryCreateBody(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        I32CallEvaluator calls,
        out AssignmentPlan[] body,
        out long bodyFuel)
    {
        body = [];
        bodyFuel = 0;
        if (statements.Count == 0)
        {
            return false;
        }

        var plans = new AssignmentPlan[statements.Count];
        long fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not AssignmentStatement assignment ||
                !I32ExpressionPlan.TryCreate(assignment.Value, frame, "", calls, out var expression))
            {
                return false;
            }

            var targetSlot = frame.GetSlot(assignment.Name);
            if (!frame.IsInt32Slot(targetSlot))
            {
                return false;
            }

            plans[i] = new AssignmentPlan(targetSlot, expression);
            fuel += 1 + expression.FuelCost;
        }

        body = plans;
        bodyFuel = fuel;
        return true;
    }

    private readonly record struct AssignmentPlan(int TargetSlot, I32ExpressionPlan Expression);
}
