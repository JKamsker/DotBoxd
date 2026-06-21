namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class ValueArrayEmitter
{
    public static void Emit(
        ILGenerator il,
        IReadOnlyList<Expression> arguments,
        Action<Expression> emitExpression)
    {
        // Allocate the backing array WITHOUT charging (CreateLiteralValueArray takes no context, so it
        // cannot charge fuel/allocation or throw), populate it with the arguments evaluated in order,
        // then charge fuel/allocation at the end. This matches the interpreter, which evaluates every
        // argument expression — running its side effects and charging its own fuel — before the call is
        // charged: a tight budget therefore can't throw QuotaExceeded before a side-effecting argument
        // runs. Interleaving each argument's evaluation (which carries its own fuel meters) with the
        // stores also keeps the instruction run metered, so this does not lengthen the meter-free span
        // the way a grouped store loop after a single charge would (which the verifier rejects for
        // calls with more than a handful of arguments).
        EmitInt32(il, arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CreateLiteralValueArray)));
        for (var i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            emitExpression(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeValueArray)));
    }
}
