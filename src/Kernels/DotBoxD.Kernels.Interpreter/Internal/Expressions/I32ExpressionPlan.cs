namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal sealed partial class I32ExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly int _value;
    private readonly int _value2;
    private readonly int _value3;
    private readonly uint _magic;
    private readonly I32ExpressionPlan? _left;
    private readonly I32ExpressionPlan? _right;

    private I32ExpressionPlan(
        ExpressionKind kind,
        int value,
        I32ExpressionPlan? left = null,
        I32ExpressionPlan? right = null,
        int value2 = 0,
        int value3 = 0,
        int? fuelCost = null)
    {
        _kind = kind;
        _value = value;
        _value2 = value2;
        _value3 = value3;
        // For the fused `(... ) % const` kinds, precompute floor(2^32 / divisor) so the runtime modulo can use an
        // exact reciprocal multiply (FastRemainder) instead of a hardware idiv. Only valid for a positive divisor.
        _magic = value3 > 0 ? (uint)((1UL << 32) / (uint)value3) : 0u;
        _left = left;
        _right = right;
        FuelCost = fuelCost ?? 1 + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0);
    }

    // Exact `a % d` for a positive constant divisor d, using the precomputed reciprocal m = floor(2^32 / d).
    // For a >= 0: q = (a*m)>>32 is floor(a/d) or one less, so r = a - q*d lands in [0, 2d) and a single
    // compare-subtract yields the exact remainder (no idiv; a*m < 2^63 so no overflow). Negative dividends fall
    // back to the checked modulo, keeping the result byte-identical to SandboxInt32Math.Remainder for all inputs.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int FastRemainder(int a, int divisor, uint magic)
    {
        if (magic == 0u || a < 0)
        {
            return SandboxInt32Math.Remainder(a, divisor);
        }

        var q = (int)(((ulong)(uint)a * magic) >> 32);
        var r = a - (q * divisor);
        return r >= divisor ? r - divisor : r;
    }

    // Exact `a / d` (truncated) for a positive constant divisor d, same reciprocal as FastRemainder: the
    // quotient estimate q = (a*m)>>32 is floor(a/d) or one less, corrected up when the remainder reaches d.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int FastDivide(int a, int divisor, uint magic)
    {
        if (magic == 0u || a < 0)
        {
            return SandboxInt32Math.Divide(a, divisor);
        }

        var q = (int)(((ulong)(uint)a * magic) >> 32);
        var r = a - (q * divisor);
        return r >= divisor ? q + 1 : q;
    }

    public int FuelCost { get; }

    public static I32ExpressionPlan InlineCall(I32ExpressionPlan body)
        => new(ExpressionKind.InlineCall, 0, body, fuelCost: body.FuelCost + 4);

    public bool TryGetRawVariableRemainderConstant(out int slot, out int divisor)
    {
        if (_kind == ExpressionKind.Remainder &&
            _left is { _kind: ExpressionKind.RawVariable } variable &&
            _right is { _kind: ExpressionKind.Literal } literal)
        {
            slot = variable._value;
            divisor = literal._value;
            return true;
        }

        slot = 0;
        divisor = 0;
        return false;
    }

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
        => TryCreate(expression, frame, assumedInt32Local, calls: null, substitutions: null, out plan);

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator calls,
        out I32ExpressionPlan plan)
        => TryCreate(expression, frame, assumedInt32Local, calls, substitutions: null, out plan);

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        IReadOnlyDictionary<string, I32ExpressionPlan>? substitutions,
        out I32ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                plan = new I32ExpressionPlan(ExpressionKind.Literal, value.Value);
                return true;
            case VariableExpression variable when substitutions?.TryGetValue(variable.Name, out var substitution) == true:
                plan = substitution;
                return true;
            case VariableExpression variable when CanReadVariable(frame, variable.Name, assumedInt32Local):
                var slot = frame.GetSlot(variable.Name);
                plan = new I32ExpressionPlan(
                    frame.IsInt32Slot(slot) ? ExpressionKind.RawVariable : ExpressionKind.BoxedVariable,
                    slot);
                return true;
            case UnaryExpression { Operator: "-" } unary
                when TryCreate(unary.Operand, frame, assumedInt32Local, calls, substitutions, out var operand):
                plan = new I32ExpressionPlan(ExpressionKind.Negate, 0, operand);
                return true;
            case BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%":
                return TryCreateSpecialBinary(binary, frame, assumedInt32Local, substitutions, out plan) ||
                       TryCreateBinary(binary, frame, assumedInt32Local, calls, substitutions, out plan);
            case CallExpression call when calls?.TryCreateInt32CallPlan(call, frame, assumedInt32Local, out var callPlan) == true:
                plan = callPlan;
                return true;
            default:
                plan = null!;
                return false;
        }
    }

    public int Evaluate(InterpreterFrame frame, SandboxContext context)
        => _kind switch
        {
            ExpressionKind.Literal => _value,
            ExpressionKind.RawVariable => frame.ReadRawInt32Slot(_value),
            ExpressionKind.BoxedVariable => frame.ReadInt32Slot(_value),
            ExpressionKind.Negate => SandboxInt32Math.Negate(_left!.Evaluate(frame, context)),
            ExpressionKind.InlineCall => EvaluateInlineCall(frame, context),
            ExpressionKind.RemainderAddRawRawConst => FastRemainder(
                SandboxInt32Math.Add(frame.ReadRawInt32Slot(_value), frame.ReadRawInt32Slot(_value2)),
                _value3,
                _magic),
            ExpressionKind.RemainderAddRawConstConst => FastRemainder(
                SandboxInt32Math.Add(frame.ReadRawInt32Slot(_value), _value2),
                _value3,
                _magic),
            ExpressionKind.RemainderByConst => FastRemainder(_left!.Evaluate(frame, context), _value3, _magic),
            ExpressionKind.DivideByConst => FastDivide(_left!.Evaluate(frame, context), _value3, _magic),
            ExpressionKind.AddRawMultiplyRawConst => SandboxInt32Math.Add(
                frame.ReadRawInt32Slot(_value),
                SandboxInt32Math.Multiply(frame.ReadRawInt32Slot(_value2), _value3)),
            ExpressionKind.Add => SandboxInt32Math.Add(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Subtract => SandboxInt32Math.Subtract(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Multiply => SandboxInt32Math.Multiply(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Divide => SandboxInt32Math.Divide(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Remainder => SandboxInt32Math.Remainder(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private int EvaluateInlineCall(InterpreterFrame frame, SandboxContext context)
    {
        context.EnterCall();
        try
        {
            return _left!.Evaluate(frame, context);
        }
        finally
        {
            context.ExitCall();
        }
    }

    private static bool TryCreateSpecialBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        string assumedInt32Local,
        IReadOnlyDictionary<string, I32ExpressionPlan>? substitutions,
        out I32ExpressionPlan plan)
    {
        // (raw + const) % const — resolves the slot through inline-call substitutions, so inline bodies of this
        // common modular-accumulator shape collapse to a single fused dispatch + one idiv instead of a 4-node
        // tree. Fuel is identical to the generic Remainder(Add(Raw, Lit), Lit) plan (5), so metering is unchanged.
        if (TryCreateRemainderAddRawConstConst(binary, frame, assumedInt32Local, substitutions, out plan))
        {
            return true;
        }

        // The remaining fused shapes read two frame slots directly and are not substitution-aware.
        if (substitutions is not null)
        {
            plan = null!;
            return false;
        }

        if (binary is
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add,
                Right: LiteralExpression { Value: I32Value modulo }
            } &&
            TryRawSlot(add.Left, frame, assumedInt32Local, out var leftSlot) &&
            TryRawSlot(add.Right, frame, assumedInt32Local, out var rightSlot))
        {
            plan = new I32ExpressionPlan(
                ExpressionKind.RemainderAddRawRawConst,
                leftSlot,
                value2: rightSlot,
                value3: modulo.Value,
                fuelCost: 5);
            return true;
        }

        if (binary is
            {
                Operator: "+",
                Left: VariableExpression left,
                Right: BinaryExpression
                {
                    Operator: "*",
                    Left: VariableExpression multiplied,
                    Right: LiteralExpression { Value: I32Value multiplier }
                }
            } &&
            TryRawSlot(left, frame, assumedInt32Local, out var leftSlot2) &&
            TryRawSlot(multiplied, frame, assumedInt32Local, out var multipliedSlot))
        {
            plan = new I32ExpressionPlan(
                ExpressionKind.AddRawMultiplyRawConst,
                leftSlot2,
                value2: multipliedSlot,
                value3: multiplier.Value,
                fuelCost: 5);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryCreateBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        IReadOnlyDictionary<string, I32ExpressionPlan>? substitutions,
        out I32ExpressionPlan plan)
    {
        if (!TryCreate(binary.Left, frame, assumedInt32Local, calls, substitutions, out var left) ||
            !TryCreate(binary.Right, frame, assumedInt32Local, calls, substitutions, out var right))
        {
            plan = null!;
            return false;
        }

        // `x % const` / `x / const` with a positive constant: carry the divisor + precomputed reciprocal so the
        // runtime op uses the exact reciprocal multiply (FastRemainder/FastDivide) instead of an idiv. Fuel is the
        // same as the generic Remainder/Divide plan (1 + left + right) so metering is unchanged.
        if (binary.Operator is "%" or "/" &&
            right._kind == ExpressionKind.Literal &&
            right._value > 0)
        {
            plan = new I32ExpressionPlan(
                binary.Operator == "%" ? ExpressionKind.RemainderByConst : ExpressionKind.DivideByConst,
                0,
                left: left,
                value3: right._value,
                fuelCost: 1 + left.FuelCost + right.FuelCost);
            return true;
        }

        plan = new I32ExpressionPlan(BinaryKind(binary.Operator), 0, left, right);
        return true;
    }

    private static bool CanReadVariable(InterpreterFrame frame, string name, string assumedInt32Local)
        => frame.CanReadInt32(name) || (name == assumedInt32Local && frame.IsInt32Local(name));

    private static bool TryRawSlot(Expression expression, InterpreterFrame frame, string assumedInt32Local, out int slot)
    {
        if (expression is VariableExpression variable)
        {
            return TryRawSlot(variable, frame, assumedInt32Local, out slot);
        }

        slot = 0;
        return false;
    }

    private static bool TryRawSlot(VariableExpression variable, InterpreterFrame frame, string assumedInt32Local, out int slot)
    {
        slot = frame.GetSlot(variable.Name);
        return frame.IsInt32Slot(slot) &&
               (frame.CanReadInt32(variable.Name) || variable.Name == assumedInt32Local);
    }

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private enum ExpressionKind
    {
        Literal,
        RawVariable,
        BoxedVariable,
        Negate,
        InlineCall,
        RemainderAddRawRawConst,
        RemainderAddRawConstConst,
        RemainderByConst,
        DivideByConst,
        AddRawMultiplyRawConst,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder
    }
}
