using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class NumericConversionCallEmitter
{
    public static bool TryEmit(
        CallExpression call,
        LocalStackKindPlanner stackPlan,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (call.Arguments.Count != 1)
        {
            return false;
        }

        var argument = call.Arguments[0];
        var sourceType = stackPlan.Infer(argument);
        switch (call.Name)
        {
            case "numeric.toI64" when sourceType == SandboxType.I32:
                emitAs(argument, StackKind.I32);
                il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I64)));
                return true;
            case "numeric.toF64" when sourceType == SandboxType.I32:
                emitAs(argument, StackKind.I32);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                return true;
            case "numeric.toF64" when sourceType == SandboxType.I64:
                emitAs(argument, StackKind.I64);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                return true;
            case "numeric.toI64":
            case "numeric.toF64":
                throw Unsupported($"conversion '{call.Name}' from {sourceType} is not supported by compiler");
            default:
                return false;
        }
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));
}
