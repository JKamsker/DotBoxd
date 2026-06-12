namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal static class CompiledTypeEmitter
{
    public static void EmitMetered(ILGenerator il, SandboxType type)
    {
        if (type is { Name: "List", Arguments.Count: 1 })
        {
            EmitMetered(il, type.Arguments[0]);
            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeList)));
            return;
        }

        if (type is { Name: "Map", Arguments.Count: 2 })
        {
            EmitMetered(il, type.Arguments[0]);
            EmitMetered(il, type.Arguments[1]);
            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeMap)));
            return;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        il.Emit(OpCodes.Ldstr, type.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.TypeScalar)));
    }
}
