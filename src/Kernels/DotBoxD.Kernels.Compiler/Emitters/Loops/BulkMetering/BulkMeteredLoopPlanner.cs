using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

internal sealed class BulkMeteredLoopPlanner
{
    private readonly LocalStackKindPlanner _stackPlan;

    public BulkMeteredLoopPlanner(LocalStackKindPlanner stackPlan)
    {
        _stackPlan = stackPlan;
    }

    public bool HasI32Local(string name) => _stackPlan.LocalKind(name) == StackKind.I32;

    public bool TryCreateBlock(IReadOnlyList<Statement> statements, out BulkMeteredBlockPlan block)
    {
        block = default;
        if (statements.Count == 0)
        {
            return false;
        }

        var plans = new BulkMeteredStatementPlan[statements.Count];
        var fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (!TryCreateStatement(statements[i], out plans[i]))
            {
                return false;
            }

            fuel += plans[i].AlwaysFuel;
        }

        block = new BulkMeteredBlockPlan(plans, fuel);
        return true;
    }

    public bool TryMeasureExpression(Expression expression, StackKind target, out int fuel)
    {
        fuel = 0;
        if (!CanEmitAs(expression, target))
        {
            return false;
        }

        return TryMeasureScalarExpression(expression, out fuel);
    }

    private bool TryCreateStatement(Statement statement, out BulkMeteredStatementPlan plan)
    {
        plan = default;
        if (statement is AssignmentStatement assignment)
        {
            return TryCreateAssignment(assignment, out plan);
        }

        if (statement is not IfStatement branch ||
            !TryMeasureExpression(branch.Condition, StackKind.Bool, out var conditionFuel) ||
            !TryCreateBranch(branch.Then, out var thenBranch) ||
            !TryCreateBranch(branch.Else, out var elseBranch))
        {
            return false;
        }

        plan = BulkMeteredStatementPlan.CreateBranch(branch.Condition, 1 + conditionFuel, thenBranch, elseBranch);
        return true;
    }

    private bool TryCreateBranch(IReadOnlyList<Statement> statements, out BulkMeteredBranchPlan branch)
    {
        branch = default;
        var assignments = new BulkMeteredAssignmentPlan[statements.Count];
        var fuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not AssignmentStatement assignment ||
                !TryCreateAssignmentPlan(assignment, out assignments[i], out var assignmentFuel))
            {
                return false;
            }

            fuel += assignmentFuel;
        }

        branch = new BulkMeteredBranchPlan(assignments, fuel);
        return true;
    }

    private bool TryCreateAssignment(AssignmentStatement assignment, out BulkMeteredStatementPlan plan)
    {
        plan = default;
        if (!TryCreateAssignmentPlan(assignment, out var assignmentPlan, out var fuel))
        {
            return false;
        }

        plan = BulkMeteredStatementPlan.CreateAssignment(assignmentPlan, fuel);
        return true;
    }

    private bool TryCreateAssignmentPlan(
        AssignmentStatement assignment,
        out BulkMeteredAssignmentPlan plan,
        out int fuel)
    {
        plan = default;
        fuel = 0;
        var kind = _stackPlan.LocalKind(assignment.Name);
        if (kind is not (StackKind.I32 or StackKind.I64 or StackKind.F64) ||
            !TryMeasureExpression(assignment.Value, kind, out var expressionFuel))
        {
            return false;
        }

        plan = new BulkMeteredAssignmentPlan(assignment.Name, kind, assignment.Value);
        fuel = 1 + expressionFuel;
        return true;
    }

    private bool TryMeasureScalarExpression(Expression expression, out int fuel)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value or I64Value or F64Value or BoolValue }:
                fuel = 1;
                return true;
            case VariableExpression variable when _stackPlan.LocalKind(variable.Name) != StackKind.Boxed:
                fuel = 1;
                return true;
            case UnaryExpression { Operator: "-" } unary
                when _stackPlan.Infer(unary.Operand)?.Name is "I32" or "I64" or "F64" &&
                     TryMeasureScalarExpression(unary.Operand, out var operandFuel):
                fuel = 1 + operandFuel;
                return true;
            case BinaryExpression binary when IsSupportedBinary(binary) &&
                                              TryMeasureScalarExpression(binary.Left, out var leftFuel) &&
                                              TryMeasureScalarExpression(binary.Right, out var rightFuel):
                fuel = 1 + leftFuel + rightFuel;
                return true;
            default:
                fuel = 0;
                return false;
        }
    }

    private bool IsSupportedBinary(BinaryExpression binary)
    {
        if (binary.Operator is "&&" or "||")
        {
            return false;
        }

        var left = _stackPlan.Infer(binary.Left);
        var right = _stackPlan.Infer(binary.Right);
        return left == right &&
               left is not null &&
               (IsArithmetic(binary.Operator, left) || IsComparison(binary.Operator, left));
    }

    private static bool IsArithmetic(string op, SandboxType type)
        => ((op is "+" or "-" or "*" or "/") && (type.Name is "I32" or "I64" or "F64")) ||
           (op == "%" && (type.Name is "I32" or "I64"));

    private static bool IsComparison(string op, SandboxType type)
        => (op is "==" or "!=" or "<" or "<=" or ">" or ">=") &&
           (type.Name is "I32" or "I64" or "F64");

    private bool CanEmitAs(Expression expression, StackKind target)
    {
        var type = _stackPlan.Infer(expression);
        return target switch
        {
            StackKind.I32 => type == SandboxType.I32,
            StackKind.I64 => type == SandboxType.I64,
            StackKind.F64 => type == SandboxType.F64,
            StackKind.Bool => type == SandboxType.Bool,
            _ => false
        };
    }
}

internal readonly record struct BulkMeteredAssignmentPlan(string Target, StackKind Kind, Expression Value);

internal readonly record struct BulkMeteredBranchPlan(BulkMeteredAssignmentPlan[] Assignments, int Fuel);

internal readonly record struct BulkMeteredBlockPlan(BulkMeteredStatementPlan[] Statements, int AlwaysFuel);

internal readonly record struct BulkMeteredStatementPlan(
    BulkMeteredAssignmentPlan Assignment,
    Expression? Condition,
    BulkMeteredBranchPlan Then,
    BulkMeteredBranchPlan Else,
    int AlwaysFuel)
{
    public bool IsBranch => Condition is not null;

    public static BulkMeteredStatementPlan CreateAssignment(BulkMeteredAssignmentPlan assignment, int fuel)
        => new(assignment, null, default, default, fuel);

    public static BulkMeteredStatementPlan CreateBranch(
        Expression condition,
        int fuel,
        BulkMeteredBranchPlan thenBranch,
        BulkMeteredBranchPlan elseBranch)
        => new(default, condition, thenBranch, elseBranch, fuel);
}
