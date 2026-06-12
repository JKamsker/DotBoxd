namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal static class CompiledLiteralEmitter
{
    public static void Emit(ILGenerator il, SandboxValue value)
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
                EmitContextStringCall(il, text.Value, nameof(CompiledRuntime.StringConst));
                break;
            case OpaqueIdValue id:
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, id.TypeName);
                il.Emit(OpCodes.Ldstr, id.Value);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.OpaqueIdConst)));
                break;
            case SandboxPathValue path:
                EmitContextStringCall(il, path.Value.RelativePath, nameof(CompiledRuntime.PathConst));
                break;
            case SandboxUriValue uri:
                EmitContextStringCall(il, uri.Value.Value, nameof(CompiledRuntime.UriConst));
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

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
