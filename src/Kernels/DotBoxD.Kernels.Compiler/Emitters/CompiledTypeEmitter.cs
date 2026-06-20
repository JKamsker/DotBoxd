using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class CompiledTypeEmitter
{
    public static void EmitMetered(ILGenerator il, SandboxType type)
    {
        if (type is { Name: "List", Arguments.Count: 1 })
        {
            EmitMetered(il, type.Arguments[0]);
            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeList)));
            return;
        }

        if (type is { Name: "Map", Arguments.Count: 2 })
        {
            EmitMetered(il, type.Arguments[0]);
            EmitMetered(il, type.Arguments[1]);
            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeMap)));
            return;
        }

        if (type.IsRecord)
        {
            EmitInt32(il, type.Arguments.Count);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CreateTypeArray)));
            for (var i = 0; i < type.Arguments.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                EmitInt32(il, i);
                EmitMetered(il, type.Arguments[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }

            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeRecord)));
            return;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        il.Emit(OpCodes.Ldstr, type.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeScalar)));
    }
}
