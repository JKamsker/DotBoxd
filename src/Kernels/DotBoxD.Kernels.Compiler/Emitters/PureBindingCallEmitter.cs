using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class PureBindingCallEmitter
{
    public static bool TryEmit(CallExpression call, ILGenerator il, Action<Expression> emitExpression)
    {
        switch (call.Name)
        {
            case "list.empty":
                EmitListEmpty(call, il);
                return true;
            case "list.of":
                il.Emit(OpCodes.Ldarg_0);
                ValueArrayEmitter.Emit(il, call.Arguments, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ListOf)));
                return true;
            case "list.count":
                EmitContextCall(il, emitExpression, call, nameof(Kernels.Runtime.CompiledRuntime.ListCount));
                return true;
            case "list.get":
                EmitContextCall(il, emitExpression, call, nameof(Kernels.Runtime.CompiledRuntime.ListGet));
                return true;
            case "list.add":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ListAdd)));
                return true;
            case "record.new":
                il.Emit(OpCodes.Ldarg_0);
                ValueArrayEmitter.Emit(il, call.Arguments, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.RecordNew)));
                return true;
            case "record.get":
                EmitContextCall(il, emitExpression, call, nameof(Kernels.Runtime.CompiledRuntime.RecordGet));
                return true;
            case "map.empty":
                EmitMapEmpty(call, il);
                return true;
            case "map.containsKey":
                EmitContextCall(il, emitExpression, call, nameof(Kernels.Runtime.CompiledRuntime.MapContainsKey));
                return true;
            case "map.get":
                EmitContextCall(il, emitExpression, call, nameof(Kernels.Runtime.CompiledRuntime.MapGet));
                return true;
            case "map.set":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.MapSet)));
                return true;
            case "map.remove":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.MapRemove)));
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

    private static void EmitContextCall(
        ILGenerator il,
        Action<Expression> emitExpression,
        CallExpression call,
        string runtimeMethod)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitArguments(call, emitExpression);
        il.Emit(OpCodes.Call, Runtime(runtimeMethod));
    }

    private static void EmitArguments(CallExpression call, Action<Expression> emitExpression)
    {
        foreach (var argument in call.Arguments)
        {
            emitExpression(argument);
        }
    }

    private static void EmitMapEmpty(CallExpression call, ILGenerator il)
    {
        if (call.GenericType is not { Name: "Map", Arguments.Count: 2 } mapType)
        {
            throw Unsupported("map.empty requires Map<K,V> genericType");
        }

        il.Emit(OpCodes.Ldarg_0);
        EmitSandboxType(il, mapType.Arguments[0]);
        EmitSandboxType(il, mapType.Arguments[1]);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.MapEmpty)));
    }

    private static void EmitListEmpty(CallExpression call, ILGenerator il)
    {
        if (call.GenericType is not { } itemType)
        {
            throw Unsupported("list.empty requires genericType");
        }

        il.Emit(OpCodes.Ldarg_0);
        EmitSandboxType(il, itemType);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ListEmpty)));
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
