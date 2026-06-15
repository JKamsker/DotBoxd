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
    private readonly ulong _magic;
    private readonly I64ExpressionPlan? _left;
    private readonly I64ExpressionPlan? _right;

    private I64ExpressionPlan(ExpressionKind kind, int slot = 0, long literal = 0, ulong magic = 0, I64ExpressionPlan? left = null, I64ExpressionPlan? right = null)
    {
        _kind = kind;
        _slot = slot;
        _literal = literal;
        _magic = magic;
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
            ExpressionKind.RemainderByConst => FastRemainder(_left!.Evaluate(frame), _literal, _magic),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i64 expression"))
        };

    // Exact `a % d` for a positive constant divisor d via the 64-bit reciprocal m = floor(2^64/d): for a >= 0,
    // the high 64 bits of a*m equal floor(a/d) or one less, so a single compare-subtract gives the exact
    // remainder (no idiv). Negatives / non-positive divisors fall back to the checked modulo — byte-identical.
    private static long FastRemainder(long a, long divisor, ulong magic)
    {
        if (magic == 0u || a < 0)
        {
            return SandboxInt64Math.Remainder(a, divisor);
        }

        var q = (long)System.Math.BigMul((ulong)a, magic, out _);
        var r = a - (q * divisor);
        return r >= divisor ? r - divisor : r;
    }

    private static ulong MagicFor(long divisor)
        => divisor > 1 ? (ulong)((System.UInt128.One << 64) / (System.UInt128)(ulong)divisor) : 0u;

    public static bool TryCreate(Expression expression, InterpreterFrame frame, out I64ExpressionPlan plan)
        => TryCreate(expression, frame, frame.IsSlotAssigned, out plan);

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        System.Func<int, bool> canReadSlot,
        out I64ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I64Value value }:
                plan = new I64ExpressionPlan(ExpressionKind.Literal, literal: value.Value);
                return true;
            case VariableExpression variable:
                var slot = frame.GetSlot(variable.Name);
                if (!frame.IsI64Slot(slot) || !canReadSlot(slot))
                {
                    plan = null!;
                    return false;
                }

                plan = new I64ExpressionPlan(ExpressionKind.RawVariable, slot);
                return true;
            case UnaryExpression { Operator: "-" } unary when TryCreate(unary.Operand, frame, canReadSlot, out var operand):
                plan = new I64ExpressionPlan(ExpressionKind.Negate, left: operand);
                return true;
            case BinaryExpression { Operator: "%", Right: LiteralExpression { Value: I64Value divisor } } modByConst
                when divisor.Value > 0 && TryCreate(modByConst.Left, frame, canReadSlot, out var dividend):
                plan = new I64ExpressionPlan(ExpressionKind.RemainderByConst, literal: divisor.Value, magic: MagicFor(divisor.Value), left: dividend);
                return true;
            case BinaryExpression { Operator: "+" or "-" or "*" or "/" or "%" } binary
                when TryCreate(binary.Left, frame, canReadSlot, out var left) && TryCreate(binary.Right, frame, canReadSlot, out var right):
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
        Remainder,
        RemainderByConst
    }
}
