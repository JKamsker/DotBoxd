namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

// Unboxed i32 comparison (two i32 expression plans -> bool), used as the condition of a branched i32 loop body
// so the comparison avoids boxing its operands and result. FuelCost counts nodes identically to the compiler's
// per-subexpression metering (1 + left + right).
internal sealed class I32ComparisonPlan
{
    private readonly Comparison _op;
    private readonly I32ExpressionPlan _left;
    private readonly I32ExpressionPlan _right;

    private I32ComparisonPlan(Comparison op, I32ExpressionPlan left, I32ExpressionPlan right)
    {
        _op = op;
        _left = left;
        _right = right;
        FuelCost = 1 + left.FuelCost + right.FuelCost;
    }

    public int FuelCost { get; }

    public bool Evaluate(InterpreterFrame frame, SandboxContext context)
    {
        var l = _left.Evaluate(frame, context);
        var r = _right.Evaluate(frame, context);
        return _op switch
        {
            Comparison.Lt => l < r,
            Comparison.Lte => l <= r,
            Comparison.Gt => l > r,
            Comparison.Gte => l >= r,
            Comparison.Eq => l == r,
            _ => l != r
        };
    }

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator calls,
        out I32ComparisonPlan plan)
    {
        plan = null!;
        if (expression is not BinaryExpression { Operator: "==" or "!=" or "<" or "<=" or ">" or ">=" } binary ||
            !I32ExpressionPlan.TryCreate(binary.Left, frame, assumedInt32Local, calls, out var left) ||
            !I32ExpressionPlan.TryCreate(binary.Right, frame, assumedInt32Local, calls, out var right))
        {
            return false;
        }

        var op = binary.Operator switch
        {
            "<" => Comparison.Lt,
            "<=" => Comparison.Lte,
            ">" => Comparison.Gt,
            ">=" => Comparison.Gte,
            "==" => Comparison.Eq,
            _ => Comparison.Ne
        };
        plan = new I32ComparisonPlan(op, left, right);
        return true;
    }

    private enum Comparison { Lt, Lte, Gt, Gte, Eq, Ne }
}
