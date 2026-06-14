namespace DotBoxd.Kernels;

public static class SandboxInt64Math
{
    public static long Add(long left, long right) => Checked(() => checked(left + right));

    public static long Subtract(long left, long right) => Checked(() => checked(left - right));

    public static long Multiply(long left, long right) => Checked(() => checked(left * right));

    public static long Divide(long left, long right)
    {
        if (right == 0)
        {
            throw InvalidInput("integer division by zero");
        }

        if (left == long.MinValue && right == -1)
        {
            throw InvalidInput("integer overflow");
        }

        return left / right;
    }

    public static long Remainder(long left, long right)
    {
        if (right == 0)
        {
            throw InvalidInput("integer division by zero");
        }

        if (left == long.MinValue && right == -1)
        {
            throw InvalidInput("integer overflow");
        }

        return left % right;
    }

    public static long Negate(long value) => Checked(() => checked(-value));

    private static long Checked(Func<long> operation)
    {
        try
        {
            return operation();
        }
        catch (OverflowException)
        {
            throw InvalidInput("integer overflow");
        }
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
