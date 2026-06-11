namespace SafeIR.Runtime;

using SafeIR;

public static class CompiledRuntime
{
    public static void ChargeFuel(SandboxContext context, int amount) => context.ChargeFuel(amount);

    public static void EnterCall(SandboxContext context) => context.EnterCall();

    public static void ExitCall(SandboxContext context) => context.ExitCall();

    public static void ValidateEntrypointInput(SandboxValue input, int parameterCount)
        => EntrypointBinder.ValidateInputShape(input, parameterCount);

    public static SandboxValue GetInputArgument(
        SandboxValue input,
        int index,
        int parameterCount,
        SandboxType expectedType)
        => EntrypointBinder.GetArgument(input, index, parameterCount, expectedType);

    public static SandboxValue RequireValueType(SandboxValue value, SandboxType expectedType)
    {
        EntrypointBinder.RequireType(value, expectedType, "function return type mismatch");
        return value;
    }

    public static SandboxValue I32(int value) => SandboxValue.FromInt32(value);

    public static SandboxValue F64(double value) => SandboxValue.FromDouble(value);

    public static SandboxValue Bool(bool value) => SandboxValue.FromBool(value);

    public static SandboxType TypeScalar(string name) => SandboxType.Scalar(name);

    public static SandboxType TypeList(SandboxType itemType) => SandboxType.List(itemType);

    public static SandboxType TypeMap(SandboxType keyType, SandboxType valueType) => SandboxType.Map(keyType, valueType);

    private static SandboxValue String(string value) => SandboxValue.FromString(value);

    public static SandboxValue StringConst(SandboxContext context, string value)
    {
        context.ChargeString(value);
        return SandboxValue.FromString(value);
    }

    public static int AsI32(SandboxValue value) => ((I32Value)value).Value;

    public static bool AsBool(SandboxValue value) => ((BoolValue)value).Value;

    public static double AsF64(SandboxValue value) => ((F64Value)value).Value;

    public static SandboxValue AddI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) + AsI32(right));

    public static SandboxValue SubI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) - AsI32(right));

    public static SandboxValue MulI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) * AsI32(right));

    public static SandboxValue DivI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) / AsI32(right));

    public static SandboxValue RemI32(SandboxValue left, SandboxValue right) => I32(AsI32(left) % AsI32(right));

    public static SandboxValue NegI32(SandboxValue value) => I32(-AsI32(value));

    public static SandboxValue NotBool(SandboxValue value) => Bool(!AsBool(value));

    public static SandboxValue Eq(SandboxValue left, SandboxValue right) => Bool(Equals(left, right));

    public static SandboxValue Ne(SandboxValue left, SandboxValue right) => Bool(!Equals(left, right));

    public static SandboxValue LtI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) < AsI32(right));

    public static SandboxValue LteI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) <= AsI32(right));

    public static SandboxValue GtI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) > AsI32(right));

    public static SandboxValue GteI32(SandboxValue left, SandboxValue right) => Bool(AsI32(left) >= AsI32(right));

    public static SandboxValue And(SandboxValue left, SandboxValue right) => Bool(AsBool(left) && AsBool(right));

    public static SandboxValue Or(SandboxValue left, SandboxValue right) => Bool(AsBool(left) || AsBool(right));

    public static SandboxValue StringLength(SandboxValue value) => I32(((StringValue)value).Value.Length);

    public static SandboxValue ConcatString(SandboxContext context, SandboxValue left, SandboxValue right)
    {
        var text = ((StringValue)left).Value + ((StringValue)right).Value;
        context.ChargeString(text);
        return String(text);
    }

    public static SandboxValue AbsI32(SandboxValue value)
    {
        var number = AsI32(value);
        if (number == int.MinValue) {
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
        if (minimum > maximum) {
            throw InvalidInput("math.clamp range is invalid");
        }

        return I32(Math.Clamp(AsI32(value), minimum, maximum));
    }

    public static SandboxValue SqrtF64(SandboxValue value) => SandboxValue.FromDouble(Math.Sqrt(AsF64(value)));

    public static SandboxValue FloorF64(SandboxValue value) => F64(Math.Floor(AsF64(value)));

    public static SandboxValue CeilF64(SandboxValue value) => F64(Math.Ceiling(AsF64(value)));

    public static SandboxValue RoundF64(SandboxValue value) => F64(Math.Round(AsF64(value), MidpointRounding.ToEven));

    public static SandboxValue ListOf(SandboxContext context, SandboxValue[] values)
    {
        context.ChargeAllocation(values.Length * 16);
        return ChargeValue(context, SandboxValue.FromList(values));
    }

    public static SandboxValue ListCount(SandboxValue list) => I32(AsList(list).Values.Count);

    public static SandboxValue ListGet(SandboxValue list, SandboxValue index)
    {
        var values = AsList(list).Values;
        var i = AsI32(index);
        if (i < 0 || i >= values.Count) {
            throw InvalidInput("list index is out of range");
        }

        return values[i];
    }

    public static SandboxValue ListAdd(SandboxContext context, SandboxValue list, SandboxValue item)
    {
        var source = AsList(list);
        if (item.Type != source.ItemType) {
            throw InvalidInput("list item type mismatch");
        }

        var values = source.Values.ToList();
        values.Add(item);
        context.ChargeAllocation(values.Count * 16);
        return ChargeValue(context, SandboxValue.FromList(values, source.ItemType));
    }

    public static SandboxValue MapEmpty(SandboxContext context, SandboxType keyType, SandboxType valueType)
    {
        context.ChargeAllocation(16);
        return ChargeValue(context, SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>(), keyType, valueType));
    }

    public static SandboxValue MapContainsKey(SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        return Bool(typedMap.Values.ContainsKey(key));
    }

    public static SandboxValue MapGet(SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        if (!typedMap.Values.TryGetValue(key, out var value)) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.NotFound, "map key was not found"));
        }

        return value;
    }

    public static SandboxValue MapSet(SandboxContext context, SandboxValue map, SandboxValue key, SandboxValue value)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        RequireType(value, typedMap.ValueType, "map value type mismatch");
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values) {
            [key] = value
        };
        context.ChargeAllocation(Math.Max(1, values.Count) * 32);
        return ChargeValue(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    public static SandboxValue MapRemove(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values);
        values.Remove(key);
        context.ChargeAllocation(Math.Max(1, values.Count) * 32);
        return ChargeValue(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
    {
        var descriptor = context.Bindings.GetDescriptor(id);
        context.ChargeBindingCall(descriptor);
        try {
            var value = descriptor.Invoke(context, args, context.CancellationToken).AsTask().GetAwaiter().GetResult();
            return context.ChargeBindingReturn(descriptor, value);
        }
        catch (SandboxRuntimeException) {
            throw;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.BindingFailure, $"binding '{id}' failed"));
        }
    }

    private static ListValue AsList(SandboxValue value)
        => value as ListValue ?? throw InvalidInput("expected list value");

    private static MapValue AsMap(SandboxValue value)
        => value as MapValue ?? throw InvalidInput("expected map value");

    private static void RequireType(SandboxValue value, SandboxType expected, string message)
    {
        if (value.Type != expected) {
            throw InvalidInput(message);
        }
    }

    private static SandboxValue ChargeValue(SandboxContext context, SandboxValue value)
    {
        context.ChargeValue(value);
        return value;
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
