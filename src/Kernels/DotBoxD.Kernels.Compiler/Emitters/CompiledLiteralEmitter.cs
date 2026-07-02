using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class CompiledLiteralEmitter
{
    public static void Emit(ILGenerator il, SandboxValue value)
        => EmitCharged(il, value);

    private static void EmitCharged(ILGenerator il, SandboxValue value)
    {
        switch (value)
        {
            case UnitValue:
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.Unit)));
                break;
            case I32Value i32:
                EmitInt32(il, i32.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.I32)));
                break;
            case I64Value i64:
                il.Emit(OpCodes.Ldc_I8, i64.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.I64)));
                break;
            case BoolValue boolean:
                EmitInt32(il, boolean.Value ? 1 : 0);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.Bool)));
                break;
            case F64Value f64:
                il.Emit(OpCodes.Ldc_R8, f64.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.F64)));
                break;
            case GuidValue guid:
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, guid.Value.ToString("D"));
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.GuidConst)));
                break;
            case StringValue text:
                EmitContextStringCall(il, text.Value, nameof(Kernels.Runtime.CompiledRuntime.StringConst));
                break;
            case OpaqueIdValue id:
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, id.TypeName);
                il.Emit(OpCodes.Ldstr, id.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.OpaqueIdConst)));
                break;
            case SandboxPathValue path:
                EmitStringBackedValue(
                    il,
                    path.Value.RelativePath,
                    nameof(Kernels.Runtime.CompiledRuntime.PathConst));
                break;
            case SandboxUriValue uri:
                EmitStringBackedValue(
                    il,
                    uri.Value.Value,
                    nameof(Kernels.Runtime.CompiledRuntime.UriConst));
                break;
            case ListValue list:
                EmitListLiteral(il, list);
                break;
            case MapValue map:
                EmitMapLiteral(il, map);
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
        string chargedRuntimeMethod)
    {
        EmitContextStringCall(il, value, chargedRuntimeMethod);
    }

    private static void EmitListLiteral(ILGenerator il, ListValue list)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitSandboxType(il, list.ItemType);
        EmitValueArray(il, list.Values);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ListLiteral)));
    }

    private static void EmitMapLiteral(ILGenerator il, MapValue map)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitSandboxType(il, map.KeyType);
        EmitSandboxType(il, map.ValueType);
        EmitValueArray(il, map.Values.Keys.ToArray());
        EmitValueArray(il, map.Values.Values.ToArray());
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.MapLiteral)));
    }

    private static void EmitValueArray(ILGenerator il, IReadOnlyList<SandboxValue> values)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, values.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CreateValueArray)));
        for (var i = 0; i < values.Count; i++)
        {
            if (i % 4 == 0)
            {
                CompiledMeterEmitter.Fuel(il, 1);
            }

            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            EmitCharged(il, values[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
