namespace DotBoxD.Kernels;

using System.Runtime.CompilerServices;

/// <summary>
/// Checked 32-bit integer arithmetic with sandbox error semantics. Overflow is detected with branchless
/// bit tests (no closures, no <c>try/catch</c>) so each operation is allocation-free and inlineable on the
/// compiler's unboxed fast path and the interpreter alike. Every overflow / divide-by-zero raises the same
/// <see cref="SandboxErrorCode.InvalidInput"/> error as before.
/// </summary>
public static class SandboxInt32Math
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Add(int left, int right)
    {
        var result = unchecked(left + right);
        // Overflow iff both operands share a sign that differs from the result's sign.
        if (((left ^ result) & (right ^ result)) < 0)
        {
            throw InvalidInput("integer overflow");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Subtract(int left, int right)
    {
        var result = unchecked(left - right);
        // Overflow iff the operands differ in sign and the result's sign differs from the minuend's.
        if (((left ^ right) & (left ^ result)) < 0)
        {
            throw InvalidInput("integer overflow");
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Multiply(int left, int right)
    {
        var result = (long)left * right;
        if (result < int.MinValue || result > int.MaxValue)
        {
            throw InvalidInput("integer overflow");
        }

        return (int)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Negate(int value)
    {
        if (value == int.MinValue)
        {
            throw InvalidInput("integer overflow");
        }

        return -value;
    }

    internal static int AddRepeated(int value, int delta, long count)
    {
        if (count < 0)
        {
            throw InvalidInput("repeat count must be non-negative");
        }

        try
        {
            var scaledDelta = checked((long)delta * count);
            var result = checked(value + scaledDelta);
            if (result < int.MinValue || result > int.MaxValue)
            {
                throw InvalidInput("integer overflow");
            }

            return (int)result;
        }
        catch (OverflowException)
        {
            throw InvalidInput("integer overflow");
        }
    }

    internal static int AddRemainderCycleFromZero(int value, int iterations, int divisor)
    {
        if (iterations < 0)
        {
            throw InvalidInput("repeat count must be non-negative");
        }

        if (divisor <= 0)
        {
            throw InvalidInput("integer division by zero");
        }

        var cycles = iterations / divisor;
        var remainder = iterations % divisor;
        try
        {
            var cycleSum = (long)divisor * (divisor - 1) / 2;
            var remainderSum = (long)remainder * (remainder - 1) / 2;
            var result = checked(value + checked(cycleSum * cycles) + remainderSum);
            if (result < int.MinValue || result > int.MaxValue)
            {
                throw InvalidInput("integer overflow");
            }

            return (int)result;
        }
        catch (OverflowException)
        {
            throw InvalidInput("integer overflow");
        }
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
