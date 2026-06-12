namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal static class CompiledValueEmitter
{
    public static void EmitLiteral(ILGenerator il, SandboxValue value)
    {
        switch (value)
        {
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
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, text.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.StringConst)));
                break;
            default:
                throw Unsupported("literal not supported by compiler");
        }
    }

    public static void EmitMeteredSandboxType(ILGenerator il, SandboxType type)
    {
        if (type is { Name: "List", Arguments.Count: 1 })
        {
            EmitMeteredSandboxType(il, type.Arguments[0]);
            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeList)));
            return;
        }

        if (type is { Name: "Map", Arguments.Count: 2 })
        {
            EmitMeteredSandboxType(il, type.Arguments[0]);
            EmitMeteredSandboxType(il, type.Arguments[1]);
            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeMap)));
            return;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        il.Emit(OpCodes.Ldstr, type.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeScalar)));
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
