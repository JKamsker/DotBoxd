namespace DotBoxD.Kernels;

using System.Runtime.CompilerServices;

/// <summary>
/// Checked 64-bit integer arithmetic with sandbox error semantics. Overflow is detected with branchless
/// bit tests for add/subtract and a minimized checked multiply path, so each operation is allocation-free and
/// inlineable on the compiler's unboxed fast path and the interpreter alike — mirroring
/// <see cref="SandboxInt32Math"/>. Every overflow / divide-by-zero raises the same
/// <see cref="SandboxErrorCode.InvalidInput"/> error.
/// </summary>
public static class SandboxInt64Math
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Add(long left, long right)
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
    public static long Subtract(long left, long right)
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
    public static long Multiply(long left, long right)
    {
        // The happy path is a single hardware imul + overflow flag; the try/catch only materializes on the
        // (rare) overflow throw. A 128-bit (Int128/BigMul) check would force a full 128-bit multiply on every
        // call, which is markedly slower in the non-inlined interpreter path.
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw InvalidInput("integer overflow");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Negate(long value)
    {
        if (value == long.MinValue)
        {
            throw InvalidInput("integer overflow");
        }

        return -value;
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
