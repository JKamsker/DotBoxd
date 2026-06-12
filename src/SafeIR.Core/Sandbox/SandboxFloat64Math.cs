namespace SafeIR;

public static class SandboxFloat64Math
{
    public static double Add(double left, double right) => Finite(left + right);

    public static double Subtract(double left, double right) => Finite(left - right);

    public static double Multiply(double left, double right) => Finite(left * right);

    public static double Divide(double left, double right) => Finite(left / right);

    public static double Remainder(double left, double right) => Finite(left % right);

    public static double Negate(double value) => Finite(-value);

    private static double Finite(double value)
        => double.IsFinite(value)
            ? value
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "f64 result must be finite"));
}
