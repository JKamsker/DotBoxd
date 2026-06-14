namespace DotBoxd.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxd.Kernels.Runtime;
using static DotBoxd.Kernels.Compiler.IlEmitterPrimitives;

internal static class CompiledMeterEmitter
{
    public static void Fuel(ILGenerator il, int amount)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, amount);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeFuel)));
    }

    public static void LoopIteration(ILGenerator il, int fuelAmount)
    {
        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, fuelAmount);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeLoopIteration)));
    }

    public static void EnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.EnterCall)));
    }

    public static void ExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ExitCall)));
    }
}
