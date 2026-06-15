namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

// Recognition for fused i32 plan kinds whose operands may be supplied through inline-call substitutions.
internal sealed partial class I32ExpressionPlan
{
    private static bool TryCreateRemainderAddRawConstConst(
        BinaryExpression binary,
        InterpreterFrame frame,
        string assumedInt32Local,
        IReadOnlyDictionary<string, I32ExpressionPlan>? substitutions,
        out I32ExpressionPlan plan)
    {
        plan = null!;
        if (binary is not
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add,
                Right: LiteralExpression { Value: I32Value modulo }
            })
        {
            return false;
        }

        // Accept either operand order: (raw + const) or (const + raw).
        if ((TryResolveRawSlot(add.Left, frame, assumedInt32Local, substitutions, out var slot) &&
             TryConstI32(add.Right, out var addend)) ||
            (TryResolveRawSlot(add.Right, frame, assumedInt32Local, substitutions, out slot) &&
             TryConstI32(add.Left, out addend)))
        {
            plan = new I32ExpressionPlan(
                ExpressionKind.RemainderAddRawConstConst,
                slot,
                value2: addend,
                value3: modulo.Value,
                fuelCost: 5);
            return true;
        }

        return false;
    }

    private static bool TryResolveRawSlot(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        IReadOnlyDictionary<string, I32ExpressionPlan>? substitutions,
        out int slot)
    {
        slot = 0;
        if (expression is not VariableExpression variable)
        {
            return false;
        }

        if (substitutions is not null && substitutions.TryGetValue(variable.Name, out var substitution))
        {
            if (substitution._kind == ExpressionKind.RawVariable)
            {
                slot = substitution._value;
                return true;
            }

            return false;
        }

        return TryRawSlot(variable, frame, assumedInt32Local, out slot);
    }

    private static bool TryConstI32(Expression expression, out int value)
    {
        if (expression is LiteralExpression { Value: I32Value literal })
        {
            value = literal.Value;
            return true;
        }

        value = 0;
        return false;
    }
}
