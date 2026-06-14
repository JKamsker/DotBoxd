namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class I32ModuloIndexWhileLoopRunner
{
    private const long LoopFuel = 5;
    private const int ConditionFuel = 3;
    private const int TotalAssignmentFuel = 6;
    private const int IndexAssignmentFuel = 4;

    public static bool TryRun(
        WhileStatement statement,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
    {
        if (options.EnableDebugTrace ||
            !TryCreatePlan(statement, frame, out var plan))
        {
            return false;
        }

        var start = frame.ReadRawInt32Slot(plan.IndexSlot);
        var end = frame.ReadRawInt32Slot(plan.EndSlot);
        var current = frame.ReadRawInt32Slot(plan.TargetSlot);
        if (!SandboxInt32Math.CanAddModuloIndexAccumulator(current, start, end, plan.Divisor))
        {
            return false;
        }

        context.ChargeLoopIterations(end - start, plan.FuelPerIteration);
        context.ChargeFuel(ConditionFuel);

        var value = SandboxInt32Math.AddModuloIndexAccumulator(current, start, end, plan.Divisor);
        frame.WriteRawInt32Slot(plan.TargetSlot, value);
        frame.WriteRawInt32Slot(plan.IndexSlot, end);
        context.Checkpoint();
        return true;
    }

    private static bool TryCreatePlan(
        WhileStatement statement,
        InterpreterFrame frame,
        out LoopPlan plan)
    {
        plan = default;
        if (!TryGetLessThanLocals(statement.Condition, frame, out var indexName, out var endName) ||
            statement.Body.Count != 2 ||
            !TryGetModuloAssignment(statement.Body[0], indexName, frame, out var targetName, out var divisor) ||
            !TryGetIncrement(statement.Body[1], indexName, frame))
        {
            return false;
        }

        plan = new LoopPlan(
            frame.GetSlot(targetName),
            frame.GetSlot(indexName),
            frame.GetSlot(endName),
            divisor,
            LoopFuel + ConditionFuel + TotalAssignmentFuel + IndexAssignmentFuel);
        return true;
    }

    private static bool TryGetLessThanLocals(
        Expression expression,
        InterpreterFrame frame,
        out string leftName,
        out string rightName)
    {
        leftName = "";
        rightName = "";
        if (expression is BinaryExpression
            {
                Operator: "<",
                Left: VariableExpression left,
                Right: VariableExpression right
            } &&
            frame.IsInt32Local(left.Name) &&
            frame.IsInt32Local(right.Name))
        {
            leftName = left.Name;
            rightName = right.Name;
            return true;
        }

        return false;
    }

    private static bool TryGetModuloAssignment(
        Statement statement,
        string indexName,
        InterpreterFrame frame,
        out string targetName,
        out int divisor)
    {
        targetName = "";
        divisor = 0;
        if (statement is not AssignmentStatement assignment ||
            !frame.IsInt32Local(assignment.Name) ||
            assignment.Value is not BinaryExpression
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add,
                Right: LiteralExpression { Value: I32Value value }
            } ||
            value.Value <= 0 ||
            !TryGetTargetAndIndex(add, assignment.Name, indexName))
        {
            return false;
        }

        targetName = assignment.Name;
        divisor = value.Value;
        return true;
    }

    private static bool TryGetIncrement(Statement statement, string indexName, InterpreterFrame frame)
        => statement is AssignmentStatement assignment &&
           string.Equals(assignment.Name, indexName, StringComparison.Ordinal) &&
           frame.IsInt32Local(indexName) &&
           assignment.Value is BinaryExpression { Operator: "+" } add &&
           ((IsVariable(add.Left, indexName) && IsI32(add.Right, 1)) ||
            (IsVariable(add.Right, indexName) && IsI32(add.Left, 1)));

    private static bool TryGetTargetAndIndex(BinaryExpression add, string targetName, string indexName)
        => (IsVariable(add.Left, targetName) && IsVariable(add.Right, indexName)) ||
           (IsVariable(add.Right, targetName) && IsVariable(add.Left, indexName));

    private static bool IsVariable(Expression expression, string name)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, name, StringComparison.Ordinal);

    private static bool IsI32(Expression expression, int value)
        => expression is LiteralExpression { Value: I32Value i32 } && i32.Value == value;

    private readonly record struct LoopPlan(
        int TargetSlot,
        int IndexSlot,
        int EndSlot,
        int Divisor,
        long FuelPerIteration);
}
