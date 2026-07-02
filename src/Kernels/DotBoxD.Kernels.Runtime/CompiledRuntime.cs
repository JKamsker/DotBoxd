using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Internal;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using SandboxContext = DotBoxD.Kernels.Sandbox.SandboxContext;
namespace DotBoxD.Kernels.Runtime;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using static System.Runtime.CompilerServices.MethodImplOptions;
/// <summary>
/// Generated-code ABI surface for DotBoxD.Kernels-compiled assemblies. This facade is owned by the
/// DotBoxD.Kernels compiler and verifier (see <c>VerifierTypeNames.CompiledRuntimeName</c>,
/// <c>VerificationPolicy.BoxedValueDefaults</c>, and the approved compiled binding target in
/// <c>BindingRegistryValidator</c>). Its members exist solely so that verified, generated
/// assemblies can call them; they are NOT a supported host API.
/// </summary>
/// <remarks>
/// Do not call these members from host code. Use <c>SandboxHost</c>, bindings, policies, and
/// verified modules instead. This type stays <see langword="public"/> because the CLR requires
/// generated assemblies to bind against it, but it is marked
/// <see cref="EditorBrowsableAttribute"/> <see cref="EditorBrowsableState.Never"/> so it does not
/// surface in normal IntelliSense or API discovery as supported host surface. Method names,
/// signatures, and metering semantics are generated-code ABI and may change without notice; they
/// are kept in lockstep with the verifier allowlist and runtime-facade hash.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class CompiledRuntime
{
    [MethodImpl(AggressiveInlining)] public static void ChargeFuel(SandboxContext context, int amount) => context.ChargeFuel(amount);
    [MethodImpl(AggressiveInlining)] public static void ChargeLoopIteration(SandboxContext context, int fuelAmount) => context.ChargeLoopIteration(fuelAmount);
    /// <summary>
    /// Closed-form replacement for a loop of the form <c>for i in [0,iterations): total = total + inv</c>.
    /// Reproduces the equivalent checked-arithmetic loop EXACTLY: it charges <paramref name="iterations"/>
    /// loop iterations at <paramref name="fuelPerIteration"/> each (or fewer, up to and including the
    /// iteration that would overflow), and throws the same integer-overflow error at the same iteration the
    /// loop would. Because the result is computed in O(1) instead of O(n) and there is no loop back-edge, the
    /// per-iteration metering call is eliminated.
    ///
    /// EXPERIMENT TRADEOFF (exp/closed-form-accumulation): adds one trusted runtime primitive to the verifier
    /// allowlist. No safety guarantee is weakened — charged loop-iterations/fuel and the overflow-throw point
    /// are identical to the loop, the bound checks still fire, and the verifier still requires per-iteration
    /// metering on every *actual* loop (this just isn't one). The cost is a small increase in trusted-runtime
    /// surface that must stay in lockstep with the verifier.
    /// </summary>
    public static int AccumulateLinearI32(SandboxContext context, int total, int inv, int iterations, int fuelPerIteration)
    {
        if (iterations <= 0)
        {
            return total;
        }
        if (inv == 0)
        {
            context.ChargeLoopIterations(iterations, fuelPerIteration);
            return total;
        }
        long start = total;
        long step = inv;
        // First 1-based iteration k at which (total + inv*k) leaves the Int32 range. The running sum is
        // monotonic in k (increasing for inv>0, decreasing for inv<0), so this is the exact iteration the
        // checked add in the loop body would throw on.
        long firstOverflow = inv > 0
            ? (int.MaxValue - start) / step + 1
            : (start - int.MinValue) / -step + 1;
        if (firstOverflow <= iterations)
        {
            // The overflowing iteration charges its loop meter before the add throws, so charge exactly that
            // many iterations, then raise the same overflow error the loop would.
            context.ChargeLoopIterations((int)firstOverflow, fuelPerIteration);
            throw InvalidInput("integer overflow");
        }
        context.ChargeLoopIterations(iterations, fuelPerIteration);
        return (int)(start + step * iterations);
    }
    [MethodImpl(AggressiveInlining)] public static void ChargeBindingCall(SandboxContext context, string id) => context.ChargeBindingCall(context.GetBindingDescriptor(id));
    public static void EnterCall(SandboxContext context) => context.EnterCall();
    public static void ExitCall(SandboxContext context) => context.ExitCall();
    [MethodImpl(AggressiveInlining)] public static void EnterInlineCall(SandboxContext context) => context.EnterCall();
    [MethodImpl(AggressiveInlining)] public static void ExitInlineCall(SandboxContext context) => context.ExitCall();
    public static void ValidateEntrypointInput(SandboxValue input, int parameterCount)
        => EntrypointBinder.ValidateInputShape(input, parameterCount);
    public static SandboxValue GetInputArgument(SandboxValue input, int index, int parameterCount, SandboxType expectedType)
        => EntrypointBinder.GetArgument(input, index, parameterCount, expectedType);
    public static SandboxValue RequireValueType(SandboxValue value, SandboxType expectedType)
    {
        EntrypointBinder.RequireType(value, expectedType, "function return type mismatch");
        return value;
    }
    public static SandboxValue Unit() => SandboxValue.Unit;
    [MethodImpl(AggressiveInlining)] public static SandboxValue I32(int value) => SandboxValue.FromInt32(value);
    public static SandboxValue I64(long value) => SandboxValue.FromInt64(value);
    public static SandboxValue F64(double value)
        => double.IsFinite(value) ? SandboxValue.FromDouble(value) : throw InvalidInput("f64 value must be finite");
    [MethodImpl(AggressiveInlining)] public static SandboxValue Bool(bool value) => SandboxValue.FromBool(value);
    private static SandboxValue String(string value) => SandboxValue.FromString(value);
    public static SandboxValue StringConst(SandboxContext context, string value)
    {
        context.ChargeString(value);
        return SandboxValue.FromString(value);
    }
    public static SandboxValue OpaqueIdConst(SandboxContext context, string typeName, string value)
    {
        context.ChargeString(value);
        return SandboxValue.FromOpaqueId(typeName, value);
    }
    public static SandboxValue PathConst(SandboxContext context, string value)
    {
        context.ChargeString(value);
        return SandboxValue.FromPath(value);
    }
    public static SandboxValue UriConst(SandboxContext context, string value)
    {
        context.ChargeString(value);
        return SandboxValue.FromUri(value);
    }
    public static SandboxValue StringLiteralValue(string value) => SandboxValue.FromString(value);
    public static SandboxValue OpaqueIdLiteralValue(string typeName, string value)
        => SandboxValue.FromOpaqueId(typeName, value);
    public static SandboxValue GuidLiteralValue(string value)
        => SandboxValue.FromGuid(System.Guid.ParseExact(value, "D"));
    public static SandboxValue PathLiteralValue(string value) => SandboxValue.FromPath(value);
    public static SandboxValue UriLiteralValue(string value) => SandboxValue.FromUri(value);
    [MethodImpl(AggressiveInlining)] public static int AsI32(SandboxValue value) => ((I32Value)value).Value;
    public static long AsI64(SandboxValue value) => ((I64Value)value).Value;
    [MethodImpl(AggressiveInlining)] public static bool AsBool(SandboxValue value) => ((BoolValue)value).Value;
    public static double AsF64(SandboxValue value) => ((F64Value)value).Value;
    public static SandboxValue AddI32(SandboxValue left, SandboxValue right) => I32(SandboxInt32Math.Add(AsI32(left), AsI32(right)));
    public static SandboxValue SubI32(SandboxValue left, SandboxValue right) => I32(SandboxInt32Math.Subtract(AsI32(left), AsI32(right)));
    public static SandboxValue MulI32(SandboxValue left, SandboxValue right) => I32(SandboxInt32Math.Multiply(AsI32(left), AsI32(right)));
    public static SandboxValue DivI32(SandboxValue left, SandboxValue right) => I32(SandboxInt32Math.Divide(AsI32(left), AsI32(right)));
    public static SandboxValue RemI32(SandboxValue left, SandboxValue right) => I32(SandboxInt32Math.Remainder(AsI32(left), AsI32(right)));
    public static SandboxValue NegI32(SandboxValue value) => I32(SandboxInt32Math.Negate(AsI32(value)));
    public static SandboxValue Neg(SandboxValue value) => SandboxNumericOperations.Negate(value);
    public static SandboxValue Add(SandboxValue left, SandboxValue right) => SandboxNumericOperations.Add(left, right);
    public static SandboxValue Sub(SandboxValue left, SandboxValue right) => SandboxNumericOperations.Subtract(left, right);
    public static SandboxValue Mul(SandboxValue left, SandboxValue right) => SandboxNumericOperations.Multiply(left, right);
    public static SandboxValue Div(SandboxValue left, SandboxValue right) => SandboxNumericOperations.Divide(left, right);
    public static SandboxValue Rem(SandboxValue left, SandboxValue right) => SandboxNumericOperations.Remainder(left, right);
    public static SandboxValue NotBool(SandboxValue value) => Bool(!AsBool(value));
    public static SandboxValue Eq(SandboxValue left, SandboxValue right) => Bool(Equals(left, right));
    public static SandboxValue Ne(SandboxValue left, SandboxValue right) => Bool(!Equals(left, right));

    public static SandboxValue LtI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) < AsI32(right));
    public static SandboxValue LteI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) <= AsI32(right));
    public static SandboxValue GtI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) > AsI32(right));
    public static SandboxValue GteI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) >= AsI32(right));
    public static SandboxValue Lt(SandboxValue left, SandboxValue right) => SandboxNumericOperations.LessThan(left, right);
    public static SandboxValue Lte(SandboxValue left, SandboxValue right) => SandboxNumericOperations.LessThanOrEqual(left, right);
    public static SandboxValue Gt(SandboxValue left, SandboxValue right) => SandboxNumericOperations.GreaterThan(left, right);
    public static SandboxValue Gte(SandboxValue left, SandboxValue right) => SandboxNumericOperations.GreaterThanOrEqual(left, right);
    public static SandboxValue And(SandboxValue left, SandboxValue right) => Bool(AsBool(left) && AsBool(right));

    public static SandboxValue Or(SandboxValue left, SandboxValue right) => Bool(AsBool(left) || AsBool(right));

    public static SandboxValue Int32ToStringInvariant(SandboxContext context, SandboxValue value)
    {
        var text = AsI32(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.ChargeString(text);
        return SandboxValue.FromString(text);
    }

    public static SandboxValue StringLength(SandboxValue value) => I32(StringLengthRaw(value));

    public static SandboxValue ConcatString(SandboxContext context, SandboxValue left, SandboxValue right)
    {
        var text = context.CreateChargedStringConcat(((StringValue)left).Value, ((StringValue)right).Value);
        return SandboxValue.FromString(text);
    }

    public static SandboxValue AbsI32(SandboxValue value)
    {
        var number = AsI32(value);
        if (number == int.MinValue)
        {
            throw InvalidInput("math.abs overflow");
        }

        return I32(Math.Abs(number));
    }

    public static SandboxValue MinI32(SandboxValue left, SandboxValue right) => I32(Math.Min(AsI32(left), AsI32(right)));

    public static SandboxValue MaxI32(SandboxValue left, SandboxValue right) => I32(Math.Max(AsI32(left), AsI32(right)));

    public static SandboxValue ClampI32(SandboxValue value, SandboxValue min, SandboxValue max)
    {
        var minimum = AsI32(min);
        var maximum = AsI32(max);
        if (minimum > maximum)
        {
            throw InvalidInput("math.clamp range is invalid");
        }

        return I32(Math.Clamp(AsI32(value), minimum, maximum));
    }

    public static SandboxValue SqrtF64(SandboxValue value) => F64(Math.Sqrt(AsF64(value)));

    public static SandboxValue FloorF64(SandboxValue value) => F64(Math.Floor(AsF64(value)));

    public static SandboxValue CeilF64(SandboxValue value) => F64(Math.Ceiling(AsF64(value)));

    public static SandboxValue RoundF64(SandboxValue value) => F64(Math.Round(AsF64(value), MidpointRounding.ToEven));

    // List and record collection entry points live in CompiledRuntime.Collections.cs (same partial type).

    public static SandboxValue MapEmpty(SandboxContext context, SandboxType keyType, SandboxType valueType)
        => CompiledMapRuntime.Empty(context, keyType, valueType);

    public static SandboxValue MapLiteral(
        SandboxContext context,
        SandboxType keyType,
        SandboxType valueType,
        SandboxValue[] keys,
        SandboxValue[] values)
        => CompiledLiteralRuntime.MapLiteral(context, keyType, valueType, keys, values);

    public static SandboxValue MapLiteralValue(
        SandboxType keyType,
        SandboxType valueType,
        SandboxValue[] keys,
        SandboxValue[] values)
        => CompiledLiteralRuntime.MapLiteralValue(keyType, valueType, keys, values);

    public static SandboxValue MapContainsKey(SandboxContext context, SandboxValue map, SandboxValue key)
        => CompiledMapRuntime.ContainsKey(context, map, key);

    public static SandboxValue MapGet(SandboxContext context, SandboxValue map, SandboxValue key)
        => CompiledMapRuntime.Get(context, map, key);

    public static SandboxValue MapSet(SandboxContext context, SandboxValue map, SandboxValue key, SandboxValue value)
        => CompiledMapRuntime.Set(context, map, key, value);

    public static SandboxValue MapRemove(SandboxContext context, SandboxValue map, SandboxValue key)
        => CompiledMapRuntime.Remove(context, map, key);

    public static void ChargeValueArray(SandboxContext context, int count)
    {
        if (count < 0)
        {
            throw InvalidInput("array length must be non-negative");
        }

        var elementCount = Math.Max(1L, count);
        context.ChargeFuel(elementCount);
        context.ChargeAllocation(checked(elementCount * 8));
    }

    public static SandboxValue[] CreateValueArray(SandboxContext context, int count)
    {
        ChargeValueArray(context, count);

        // Zero-argument compiled binding calls do not need a fresh heap array: the emitter never stores into it
        // (no Stelem_Ref is emitted for an empty argument list), and an empty array is immutable.
        // Fuel/allocation charges above stay unchanged, so observable resource accounting stays identical
        // to allocating the array.
        if (count == 0)
        {
            return Array.Empty<SandboxValue>();
        }

        return new SandboxValue[count];
    }

    public static SandboxValue[] CreateLiteralValueArray(int count)
        => CompiledLiteralRuntime.CreateValueArray(count);

    private static ListValue AsList(SandboxValue value)
    {
        var list = value as ListValue ?? throw InvalidInput("expected list value");
        SandboxValueValidator.RequireType(list, list.Type, "list item type mismatch");
        return list;
    }

    // Read-only collection operations only need the runtime kind, not a recursive
    // re-walk of every element. Collection contents are already validated against
    // their declared element types at trust boundaries (entrypoint inputs via
    // EntrypointBinder and binding returns via ChargeBindingReturn) and stay typed
    // through every internal constructor, so reads can trust the snapshotted value.
    private static ListValue AsListReadOnly(SandboxValue value)
        => value as ListValue ?? throw InvalidInput("expected list value");

    private static RecordValue AsRecordReadOnly(SandboxValue value)
        => value as RecordValue ?? throw InvalidInput("expected record value");

    private static SandboxValue ChargeValue(SandboxContext context, SandboxValue value)
    {
        context.ChargeValue(value);
        return value;
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
