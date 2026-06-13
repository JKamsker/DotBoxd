namespace SafeIR.Runtime;

using SafeIR;

public static class CompiledRuntime
{
    public static void ChargeFuel(SandboxContext context, int amount) => context.ChargeFuel(amount);
    public static void ChargeLoopIteration(SandboxContext context, int fuelAmount)
        => context.ChargeLoopIteration(fuelAmount);
    public static void EnterCall(SandboxContext context) => context.EnterCall();
    public static void ExitCall(SandboxContext context) => context.ExitCall();

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
    public static SandboxValue I32(int value) => SandboxValue.FromInt32(value);
    public static SandboxValue I64(long value) => SandboxValue.FromInt64(value);
    public static SandboxValue F64(double value)
        => double.IsFinite(value) ? SandboxValue.FromDouble(value) : throw InvalidInput("f64 value must be finite");
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

    public static SandboxValue PathLiteralValue(string value) => SandboxValue.FromPath(value);

    public static SandboxValue UriLiteralValue(string value) => SandboxValue.FromUri(value);

    public static int AsI32(SandboxValue value) => ((I32Value)value).Value;
    public static long AsI64(SandboxValue value) => ((I64Value)value).Value;
    public static bool AsBool(SandboxValue value) => ((BoolValue)value).Value;
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

    public static SandboxValue StringLength(SandboxValue value) => I32(((StringValue)value).Value.Length);

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

    public static SandboxValue ListOf(SandboxContext context, SandboxValue[] values)
    {
        context.ChargeFuel(SandboxCollectionFuel.Copy(values.Length));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(values.Length, 16));
        return ChargeValue(context, SandboxValue.FromList(values));
    }

    public static SandboxValue ListLiteral(SandboxContext context, SandboxType itemType, SandboxValue[] values)
        => CompiledLiteralRuntime.ListLiteral(context, itemType, values);

    public static SandboxValue ListLiteralValue(SandboxType itemType, SandboxValue[] values)
        => CompiledLiteralRuntime.ListLiteralValue(itemType, values);

    public static SandboxValue ListEmpty(SandboxContext context, SandboxType itemType)
    {
        context.ChargeFuel(SandboxCollectionFuel.Empty());
        context.ChargeAllocation(8);
        return ChargeValue(context, SandboxValue.FromList([], itemType));
    }

    public static SandboxValue ListCount(SandboxContext context, SandboxValue list)
    {
        var values = AsList(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        return I32(values.Count);
    }

    public static SandboxValue ListGet(SandboxContext context, SandboxValue list, SandboxValue index)
    {
        var values = AsList(list).Values;
        context.ChargeFuel(SandboxCollectionFuel.Read(values.Count));
        var i = AsI32(index);
        if (i < 0 || i >= values.Count)
        {
            throw InvalidInput("list index is out of range");
        }

        return values[i];
    }

    public static SandboxValue ListAdd(SandboxContext context, SandboxValue list, SandboxValue item)
    {
        var source = AsList(list);
        if (item.Type != source.ItemType)
        {
            throw InvalidInput("list item type mismatch");
        }

        context.ChargeFuel(SandboxCollectionFuel.Copy(source.Values.Count, addedCount: 1));
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            source.Values.Count,
            addedCount: 1,
            bytesPerElement: 16));
        var values = source.Values.ToList();
        values.Add(item);
        return ChargeValue(context, SandboxValue.FromList(values, source.ItemType));
    }

    public static SandboxValue MapEmpty(SandboxContext context, SandboxType keyType, SandboxType valueType)
    {
        context.ChargeFuel(SandboxCollectionFuel.Empty());
        context.ChargeAllocation(16);
        return ChargeValue(context, SandboxValue.FromMap(new Dictionary<SandboxValue, SandboxValue>(), keyType, valueType));
    }

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
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        return Bool(typedMap.Values.ContainsKey(key));
    }

    public static SandboxValue MapGet(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Read(typedMap.Values.Count));
        if (!typedMap.Values.TryGetValue(key, out var value))
        {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.NotFound, "map key was not found"));
        }

        return value;
    }

    public static SandboxValue MapSet(SandboxContext context, SandboxValue map, SandboxValue key, SandboxValue value)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count, addedCount: 1));
        RequireType(value, typedMap.ValueType, "map value type mismatch");
        var addedCount = typedMap.Values.ContainsKey(key) ? 0 : 1;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(
            typedMap.Values.Count,
            addedCount,
            bytesPerElement: 32,
            minimumOne: true));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values)
        {
            [key] = value
        };
        return ChargeValue(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    public static SandboxValue MapRemove(SandboxContext context, SandboxValue map, SandboxValue key)
    {
        var typedMap = AsMap(map);
        RequireType(key, typedMap.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(typedMap.Values.Count));
        var count = typedMap.Values.ContainsKey(key) ? typedMap.Values.Count - 1 : typedMap.Values.Count;
        context.ChargeAllocation(SandboxCollectionFuel.AllocationBytes(count, 32, minimumOne: true));
        var values = new Dictionary<SandboxValue, SandboxValue>(typedMap.Values);
        values.Remove(key);
        return ChargeValue(context, SandboxValue.FromMap(values, typedMap.KeyType, typedMap.ValueType));
    }

    public static SandboxValue CallBinding(SandboxContext context, string id, SandboxValue[] args)
        => CompiledBindingDispatcher.CallBinding(context, id, args);

    public static SandboxValue[] CreateValueArray(SandboxContext context, int count)
    {
        if (count < 0)
        {
            throw InvalidInput("array length must be non-negative");
        }

        var elementCount = Math.Max(1L, count);
        context.ChargeFuel(elementCount);
        context.ChargeAllocation(checked(elementCount * 8));
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

    private static MapValue AsMap(SandboxValue value)
    {
        var map = value as MapValue ?? throw InvalidInput("expected map value");
        SandboxValueValidator.RequireType(map, map.Type, "map entry type mismatch");
        return map;
    }

    private static SandboxValue RequireType(SandboxValue value, SandboxType expected, string message)
    {
        SandboxValueValidator.RequireType(value, expected, message);
        return value;
    }

    private static SandboxValue ChargeValue(SandboxContext context, SandboxValue value)
    {
        context.ChargeValue(value);
        return value;
    }

    private static SandboxRuntimeException InvalidInput(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
