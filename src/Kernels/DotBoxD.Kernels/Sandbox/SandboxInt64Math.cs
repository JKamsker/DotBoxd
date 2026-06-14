namespace DotBoxD.Kernels;

public static class SandboxInt64Math
{
    // Inline checked arithmetic (no Func<long> lambda — that allocated a capturing closure on every op, which
    // dominated i64 arithmetic in both the interpreter and compiled paths). Semantics are identical: overflow
    // throws an InvalidInput "integer overflow".
    public static long Add(long left, long right)
    {
        try { return checked(left + right); }
        catch (OverflowException) { throw InvalidInput("integer overflow"); }
    }

    public static long Subtract(long left, long right)
    {
        try { return checked(left - right); }
        catch (OverflowException) { throw InvalidInput("integer overflow"); }
    }

    public static long Multiply(long left, long right)
    {
        try { return checked(left * right); }
        catch (OverflowException) { throw InvalidInput("integer overflow"); }
    }

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

    public static long Negate(long value)
    {
        try { return checked(-value); }
        catch (OverflowException) { throw InvalidInput("integer overflow"); }
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
