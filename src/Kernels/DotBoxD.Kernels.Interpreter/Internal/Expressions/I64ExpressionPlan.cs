namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

// Unboxed i64 expression plan: i64 literals, raw i64 locals, and checked i64 arithmetic (via SandboxInt64Math,
// identical overflow semantics to the boxed path). Narrow by design — anything else (boxed i64 locals, calls,
// cross-type operands) makes TryCreate fail and the loop falls back to the boxed evaluator.
internal sealed class I64ExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly int _slot;
    private readonly long _literal;
    private readonly I64ExpressionPlan? _left;
    private readonly I64ExpressionPlan? _right;

    private I64ExpressionPlan(ExpressionKind kind, int slot = 0, long literal = 0, I64ExpressionPlan? left = null, I64ExpressionPlan? right = null)
    {
        _kind = kind;
        _slot = slot;
        _literal = literal;
        _left = left;
        _right = right;
        FuelCost = 1 + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0);
    }

    public int FuelCost { get; }

    public long Evaluate(InterpreterFrame frame)
        => _kind switch
        {
            ExpressionKind.Literal => _literal,
            ExpressionKind.RawVariable => frame.ReadRawInt64Slot(_slot),
            ExpressionKind.Negate => SandboxInt64Math.Negate(_left!.Evaluate(frame)),
            ExpressionKind.Add => SandboxInt64Math.Add(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Subtract => SandboxInt64Math.Subtract(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Multiply => SandboxInt64Math.Multiply(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Divide => SandboxInt64Math.Divide(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Remainder => SandboxInt64Math.Remainder(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i64 expression"))
        };

    public static bool TryCreate(Expression expression, InterpreterFrame frame, out I64ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I64Value value }:
                plan = new I64ExpressionPlan(ExpressionKind.Literal, literal: value.Value);
                return true;
            case VariableExpression variable when frame.IsI64Slot(frame.GetSlot(variable.Name)):
                plan = new I64ExpressionPlan(ExpressionKind.RawVariable, frame.GetSlot(variable.Name));
                return true;
            case UnaryExpression { Operator: "-" } unary when TryCreate(unary.Operand, frame, out var operand):
                plan = new I64ExpressionPlan(ExpressionKind.Negate, left: operand);
                return true;
            case BinaryExpression { Operator: "+" or "-" or "*" or "/" or "%" } binary
                when TryCreate(binary.Left, frame, out var left) && TryCreate(binary.Right, frame, out var right):
                plan = new I64ExpressionPlan(BinaryKind(binary.Operator), left: left, right: right);
                return true;
            default:
                plan = null!;
                return false;
        }
    }

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i64 expression"))
        };

    private enum ExpressionKind
    {
        Literal,
        RawVariable,
        Negate,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder
    }
}
