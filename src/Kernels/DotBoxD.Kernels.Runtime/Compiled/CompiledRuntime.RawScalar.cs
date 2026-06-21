using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

using static System.Runtime.CompilerServices.MethodImplOptions;

// Unboxed scalar ABI helpers: arithmetic and comparisons that keep operands/results as raw int/long/double/bool
// on the IL stack (no SandboxValue allocation). Semantics are identical to the boxed forms — i32/i64 via the
// branchless/checked SandboxInt32Math/SandboxInt64Math, f64 via SandboxFloat64Math (per-op finiteness),
// comparisons are pure (sandbox f64 values are always finite, so ordered f64 comparisons have no NaN case).
public static partial class CompiledRuntime
{
    [MethodImpl(AggressiveInlining)] public static int AddI32Raw(int left, int right) => SandboxInt32Math.Add(left, right);
    [MethodImpl(AggressiveInlining)] public static int SubI32Raw(int left, int right) => SandboxInt32Math.Subtract(left, right);
    [MethodImpl(AggressiveInlining)] public static int MulI32Raw(int left, int right) => SandboxInt32Math.Multiply(left, right);
    [MethodImpl(AggressiveInlining)] public static int DivI32Raw(int left, int right) => SandboxInt32Math.Divide(left, right);
    [MethodImpl(AggressiveInlining)] public static int RemI32Raw(int left, int right) => SandboxInt32Math.Remainder(left, right);
    [MethodImpl(AggressiveInlining)] public static int AddRemI32Raw(int left, int right, int divisor) => SandboxInt32Math.Remainder(SandboxInt32Math.Add(left, right), divisor);
    [MethodImpl(AggressiveInlining)] public static int NegI32Raw(int value) => SandboxInt32Math.Negate(value);
    [MethodImpl(AggressiveInlining)] public static int AbsI32Raw(int value) => value == int.MinValue ? throw InvalidInput("math.abs overflow") : Math.Abs(value);
    [MethodImpl(AggressiveInlining)] public static int MinI32Raw(int left, int right) => Math.Min(left, right);
    [MethodImpl(AggressiveInlining)] public static int MaxI32Raw(int left, int right) => Math.Max(left, right);
    [MethodImpl(AggressiveInlining)]
    public static int ClampI32Raw(int value, int min, int max)
        => min <= max ? Math.Clamp(value, min, max) : throw InvalidInput("math.clamp range is invalid");

    [MethodImpl(AggressiveInlining)] public static double AddF64Raw(double left, double right) => SandboxFloat64Math.Add(left, right);
    [MethodImpl(AggressiveInlining)] public static double SubF64Raw(double left, double right) => SandboxFloat64Math.Subtract(left, right);
    [MethodImpl(AggressiveInlining)] public static double MulF64Raw(double left, double right) => SandboxFloat64Math.Multiply(left, right);
    [MethodImpl(AggressiveInlining)] public static double DivF64Raw(double left, double right) => SandboxFloat64Math.Divide(left, right);

    [MethodImpl(AggressiveInlining)] public static bool LtI32Raw(int left, int right) => left < right;
    [MethodImpl(AggressiveInlining)] public static bool LteI32Raw(int left, int right) => left <= right;
    [MethodImpl(AggressiveInlining)] public static bool GtI32Raw(int left, int right) => left > right;
    [MethodImpl(AggressiveInlining)] public static bool GteI32Raw(int left, int right) => left >= right;
    [MethodImpl(AggressiveInlining)] public static bool EqI32Raw(int left, int right) => left == right;
    [MethodImpl(AggressiveInlining)] public static bool NeI32Raw(int left, int right) => left != right;

    [MethodImpl(AggressiveInlining)] public static long AddI64Raw(long left, long right) => SandboxInt64Math.Add(left, right);
    [MethodImpl(AggressiveInlining)] public static long SubI64Raw(long left, long right) => SandboxInt64Math.Subtract(left, right);
    [MethodImpl(AggressiveInlining)] public static long MulI64Raw(long left, long right) => SandboxInt64Math.Multiply(left, right);
    [MethodImpl(AggressiveInlining)] public static long DivI64Raw(long left, long right) => SandboxInt64Math.Divide(left, right);
    [MethodImpl(AggressiveInlining)] public static long RemI64Raw(long left, long right) => SandboxInt64Math.Remainder(left, right);
    [MethodImpl(AggressiveInlining)] public static long NegI64Raw(long value) => SandboxInt64Math.Negate(value);

    [MethodImpl(AggressiveInlining)] public static bool LtI64Raw(long left, long right) => left < right;
    [MethodImpl(AggressiveInlining)] public static bool LteI64Raw(long left, long right) => left <= right;
    [MethodImpl(AggressiveInlining)] public static bool GtI64Raw(long left, long right) => left > right;
    [MethodImpl(AggressiveInlining)] public static bool GteI64Raw(long left, long right) => left >= right;
    [MethodImpl(AggressiveInlining)] public static bool EqI64Raw(long left, long right) => left == right;
    [MethodImpl(AggressiveInlining)] public static bool NeI64Raw(long left, long right) => left != right;

    [MethodImpl(AggressiveInlining)] public static bool LtF64Raw(double left, double right) => left < right;
    [MethodImpl(AggressiveInlining)] public static bool LteF64Raw(double left, double right) => left <= right;
    [MethodImpl(AggressiveInlining)] public static bool GtF64Raw(double left, double right) => left > right;
    [MethodImpl(AggressiveInlining)] public static bool GteF64Raw(double left, double right) => left >= right;
    [MethodImpl(AggressiveInlining)] public static bool EqF64Raw(double left, double right) => left == right;
    [MethodImpl(AggressiveInlining)] public static bool NeF64Raw(double left, double right) => left != right;
}
