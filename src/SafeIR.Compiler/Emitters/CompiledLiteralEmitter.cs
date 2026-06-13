namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal static class CompiledLiteralEmitter
{
    public static void Emit(ILGenerator il, SandboxValue value)
        => Emit(il, value, chargeLiteral: true);

    private static void Emit(ILGenerator il, SandboxValue value, bool chargeLiteral)
    {
        switch (value)
        {
            case UnitValue:
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Unit)));
                break;
            case I32Value i32:
                EmitInt32(il, i32.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I32)));
                break;
            case I64Value i64:
                il.Emit(OpCodes.Ldc_I8, i64.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I64)));
                break;
            case BoolValue boolean:
                EmitInt32(il, boolean.Value ? 1 : 0);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Bool)));
                break;
            case F64Value f64:
                il.Emit(OpCodes.Ldc_R8, f64.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                break;
            case StringValue text:
                if (chargeLiteral)
                {
                    EmitContextStringCall(il, text.Value, nameof(CompiledRuntime.StringConst));
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, text.Value);
                    il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.StringLiteralValue)));
                }

                break;
            case OpaqueIdValue id:
                if (chargeLiteral)
                {
                    il.Emit(OpCodes.Ldarg_0);
                }

                il.Emit(OpCodes.Ldstr, id.TypeName);
                il.Emit(OpCodes.Ldstr, id.Value);
                il.Emit(OpCodes.Call, chargeLiteral
                    ? Runtime(nameof(CompiledRuntime.OpaqueIdConst))
                    : Runtime(nameof(CompiledRuntime.OpaqueIdLiteralValue)));
                break;
            case SandboxPathValue path:
                EmitStringBackedValue(
                    il,
                    path.Value.RelativePath,
                    chargeLiteral,
                    nameof(CompiledRuntime.PathConst),
                    nameof(CompiledRuntime.PathLiteralValue));
                break;
            case SandboxUriValue uri:
                EmitStringBackedValue(
                    il,
                    uri.Value.Value,
                    chargeLiteral,
                    nameof(CompiledRuntime.UriConst),
                    nameof(CompiledRuntime.UriLiteralValue));
                break;
            case ListValue list:
                EmitListLiteral(il, list, chargeLiteral);
                break;
            case MapValue map:
                EmitMapLiteral(il, map, chargeLiteral);
                break;
            default:
                throw Unsupported("literal not supported by compiler");
        }
    }

    private static void EmitContextStringCall(ILGenerator il, string value, string runtimeMethod)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, Runtime(runtimeMethod));
    }

    private static void EmitStringBackedValue(
        ILGenerator il,
        string value,
        bool chargeLiteral,
        string chargedRuntimeMethod,
        string unchargedFactoryMethod)
    {
        if (chargeLiteral)
        {
            EmitContextStringCall(il, value, chargedRuntimeMethod);
            return;
        }

        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, Runtime(unchargedFactoryMethod));
    }

    private static void EmitListLiteral(ILGenerator il, ListValue list, bool chargeLiteral)
    {
        if (chargeLiteral)
        {
            il.Emit(OpCodes.Ldarg_0);
        }

        EmitSandboxType(il, list.ItemType);
        EmitValueArray(il, list.Values);
        il.Emit(OpCodes.Call, Runtime(chargeLiteral
            ? nameof(CompiledRuntime.ListLiteral)
            : nameof(CompiledRuntime.ListLiteralValue)));
    }

    private static void EmitMapLiteral(ILGenerator il, MapValue map, bool chargeLiteral)
    {
        if (chargeLiteral)
        {
            il.Emit(OpCodes.Ldarg_0);
        }

        EmitSandboxType(il, map.KeyType);
        EmitSandboxType(il, map.ValueType);
        EmitValueArray(il, map.Values.Keys.ToArray());
        EmitValueArray(il, map.Values.Values.ToArray());
        il.Emit(OpCodes.Call, Runtime(chargeLiteral
            ? nameof(CompiledRuntime.MapLiteral)
            : nameof(CompiledRuntime.MapLiteralValue)));
    }

    private static void EmitValueArray(ILGenerator il, IReadOnlyList<SandboxValue> values)
    {
        EmitInt32(il, values.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CreateLiteralValueArray)));
        for (var i = 0; i < values.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            Emit(il, values[i], chargeLiteral: false);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
