namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal static class ValueArrayEmitter
{
    public static void Emit(
        ILGenerator il,
        IReadOnlyList<Expression> arguments,
        Action<Expression> emitExpression)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CreateValueArray)));
        for (var i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            emitExpression(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }
}
