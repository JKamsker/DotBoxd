namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal static class PureBindingCallEmitter
{
    public static bool TryEmit(CallExpression call, ILGenerator il, Action<Expression> emitExpression)
    {
        switch (call.Name) {
            case "list.of":
                il.Emit(OpCodes.Ldarg_0);
                ValueArrayEmitter.Emit(il, call.Arguments, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListOf)));
                return true;
            case "list.count":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.ListCount));
                return true;
            case "list.get":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.ListGet));
                return true;
            case "list.add":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListAdd)));
                return true;
            case "map.empty":
                EmitMapEmpty(call, il);
                return true;
            case "map.containsKey":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.MapContainsKey));
                return true;
            case "map.get":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.MapGet));
                return true;
            case "map.set":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.MapSet)));
                return true;
            case "map.remove":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.MapRemove)));
                return true;
            default:
                return false;
        }
    }

    private static void EmitCall(
        ILGenerator il,
        Action<Expression> emitExpression,
        CallExpression call,
        string runtimeMethod)
    {
        EmitArguments(call, emitExpression);
        il.Emit(OpCodes.Call, Runtime(runtimeMethod));
    }

    private static void EmitArguments(CallExpression call, Action<Expression> emitExpression)
    {
        foreach (var argument in call.Arguments) {
            emitExpression(argument);
        }
    }

    private static void EmitMapEmpty(CallExpression call, ILGenerator il)
    {
        if (call.GenericType is not { Name: "Map", Arguments.Count: 2 } mapType) {
            throw Unsupported("map.empty requires Map<K,V> genericType");
        }

        il.Emit(OpCodes.Ldarg_0);
        EmitSandboxType(il, mapType.Arguments[0]);
        EmitSandboxType(il, mapType.Arguments[1]);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.MapEmpty)));
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
