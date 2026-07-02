namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class ValueArrayEmitter
{
    public static void Emit(
        ILGenerator il,
        IReadOnlyList<Expression> arguments,
        Action<Expression> emitExpression)
    {
        // Evaluate arguments before the synthetic array allocation charge so side-effecting arguments
        // follow interpreter ordering. The backing array itself is still allocated through the
        // context-aware runtime helper, closing the verifier-admitted uncharged allocation path.
        var locals = new LocalBuilder[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            emitExpression(arguments[i]);
            locals[i] = il.DeclareLocal(typeof(SandboxValue));
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CreateValueArray)));
        for (var i = 0; i < locals.Length; i++)
        {
            if (i % 4 == 0)
            {
                CompiledMeterEmitter.Fuel(il, 1);
            }

            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            il.Emit(OpCodes.Ldloc, locals[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }
}
