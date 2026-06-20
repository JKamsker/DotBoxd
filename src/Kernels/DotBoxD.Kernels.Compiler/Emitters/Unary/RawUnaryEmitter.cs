using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class RawUnaryEmitter
{
    public static bool TryEmit(
        UnaryExpression unary,
        LocalStackKindPlanner stackPlan,
        ILGenerator il,
        Action<Expression, StackKind> emitAs,
        out StackKind kind)
    {
        kind = StackKind.Boxed;
        if (unary.Operator != "-")
        {
            return false;
        }

        switch (stackPlan.Infer(unary.Operand)?.Name)
        {
            case "I32":
                emitAs(unary.Operand, StackKind.I32);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI32Raw)));
                kind = StackKind.I32;
                return true;
            case "I64":
                emitAs(unary.Operand, StackKind.I64);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI64Raw)));
                kind = StackKind.I64;
                return true;
            case "F64":
                emitAs(unary.Operand, StackKind.F64);
                il.Emit(OpCodes.Neg);
                kind = StackKind.F64;
                return true;
            default:
                return false;
        }
    }
}
