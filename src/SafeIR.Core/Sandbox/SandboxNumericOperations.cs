namespace SafeIR;

public static class SandboxNumericOperations
{
    public static SandboxValue Negate(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromInt32(SandboxInt32Math.Negate(number.Value)),
            I64Value number => SandboxValue.FromInt64(SandboxInt64Math.Negate(number.Value)),
            F64Value number => SandboxValue.FromDouble(SandboxFloat64Math.Negate(number.Value)),
            _ => throw TypeMismatch()
        };

    public static SandboxValue Add(SandboxValue left, SandboxValue right)
        => (left, right) switch
        {
            (I32Value l, I32Value r) => SandboxValue.FromInt32(SandboxInt32Math.Add(l.Value, r.Value)),
            (I64Value l, I64Value r) => SandboxValue.FromInt64(SandboxInt64Math.Add(l.Value, r.Value)),
            (F64Value l, F64Value r) => SandboxValue.FromDouble(SandboxFloat64Math.Add(l.Value, r.Value)),
            _ => throw TypeMismatch()
        };

    public static SandboxValue Subtract(SandboxValue left, SandboxValue right)
        => (left, right) switch
        {
            (I32Value l, I32Value r) => SandboxValue.FromInt32(SandboxInt32Math.Subtract(l.Value, r.Value)),
            (I64Value l, I64Value r) => SandboxValue.FromInt64(SandboxInt64Math.Subtract(l.Value, r.Value)),
            (F64Value l, F64Value r) => SandboxValue.FromDouble(SandboxFloat64Math.Subtract(l.Value, r.Value)),
            _ => throw TypeMismatch()
        };

    public static SandboxValue Multiply(SandboxValue left, SandboxValue right)
        => (left, right) switch
        {
            (I32Value l, I32Value r) => SandboxValue.FromInt32(SandboxInt32Math.Multiply(l.Value, r.Value)),
            (I64Value l, I64Value r) => SandboxValue.FromInt64(SandboxInt64Math.Multiply(l.Value, r.Value)),
            (F64Value l, F64Value r) => SandboxValue.FromDouble(SandboxFloat64Math.Multiply(l.Value, r.Value)),
            _ => throw TypeMismatch()
        };

    public static SandboxValue Divide(SandboxValue left, SandboxValue right)
        => (left, right) switch
        {
            (I32Value l, I32Value r) => SandboxValue.FromInt32(SandboxInt32Math.Divide(l.Value, r.Value)),
            (I64Value l, I64Value r) => SandboxValue.FromInt64(SandboxInt64Math.Divide(l.Value, r.Value)),
            (F64Value l, F64Value r) => SandboxValue.FromDouble(SandboxFloat64Math.Divide(l.Value, r.Value)),
            _ => throw TypeMismatch()
        };

    public static SandboxValue Remainder(SandboxValue left, SandboxValue right)
        => (left, right) switch
        {
            (I32Value l, I32Value r) => SandboxValue.FromInt32(SandboxInt32Math.Remainder(l.Value, r.Value)),
            (I64Value l, I64Value r) => SandboxValue.FromInt64(SandboxInt64Math.Remainder(l.Value, r.Value)),
            (F64Value l, F64Value r) => SandboxValue.FromDouble(SandboxFloat64Math.Remainder(l.Value, r.Value)),
            _ => throw TypeMismatch()
        };

    public static SandboxValue LessThan(SandboxValue left, SandboxValue right)
        => SandboxValue.FromBool(Compare(left, right, static comparison => comparison < 0));

    public static SandboxValue LessThanOrEqual(SandboxValue left, SandboxValue right)
        => SandboxValue.FromBool(Compare(left, right, static comparison => comparison <= 0));

    public static SandboxValue GreaterThan(SandboxValue left, SandboxValue right)
        => SandboxValue.FromBool(Compare(left, right, static comparison => comparison > 0));

    public static SandboxValue GreaterThanOrEqual(SandboxValue left, SandboxValue right)
        => SandboxValue.FromBool(Compare(left, right, static comparison => comparison >= 0));

    private static bool Compare(SandboxValue left, SandboxValue right, Func<int, bool> result)
        => (left, right) switch
        {
            (I32Value l, I32Value r) => result(l.Value.CompareTo(r.Value)),
            (I64Value l, I64Value r) => result(l.Value.CompareTo(r.Value)),
            (F64Value l, F64Value r) => result(l.Value.CompareTo(r.Value)),
            _ => throw TypeMismatch()
        };

    private static SandboxRuntimeException TypeMismatch()
        => new(new SandboxError(SandboxErrorCode.InvalidInput, "numeric operand type mismatch"));
}
