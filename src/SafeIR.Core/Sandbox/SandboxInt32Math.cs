namespace SafeIR;

public static class SandboxInt32Math
{
    public static int Add(int left, int right) => Checked(() => checked(left + right));

    public static int Subtract(int left, int right) => Checked(() => checked(left - right));

    public static int Multiply(int left, int right) => Checked(() => checked(left * right));

    public static int Divide(int left, int right)
    {
        if (right == 0) {
            throw InvalidInput("integer division by zero");
        }

        if (left == int.MinValue && right == -1) {
            throw InvalidInput("integer overflow");
        }

        return left / right;
    }

    public static int Remainder(int left, int right)
    {
        if (right == 0) {
            throw InvalidInput("integer division by zero");
        }

        if (left == int.MinValue && right == -1) {
            throw InvalidInput("integer overflow");
        }

        return left % right;
    }

    public static int Negate(int value) => Checked(() => checked(-value));

    private static int Checked(Func<int> operation)
    {
        try {
            return operation();
        }
        catch (OverflowException) {
            throw InvalidInput("integer overflow");
        }
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
