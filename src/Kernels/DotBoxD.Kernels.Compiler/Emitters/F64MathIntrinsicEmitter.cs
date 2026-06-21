using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class F64MathIntrinsicEmitter
{
    public static bool TryEmit(
        Expression expression,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (expression is not CallExpression call ||
            call.Arguments.Count != 1 ||
            !TryGetRawIntrinsic(call, bindings, out var rawMethod))
        {
            return false;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        var operand = il.DeclareLocal(typeof(double));
        emitAs(call.Arguments[0], StackKind.F64);
        il.Emit(OpCodes.Stloc, operand);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
        il.Emit(OpCodes.Ldloc, operand);
        il.Emit(OpCodes.Call, Runtime(rawMethod));
        return true;
    }

    private static bool TryGetRawIntrinsic(CallExpression call, IBindingCatalog bindings, out string rawMethod)
    {
        rawMethod = call.Name switch
        {
            "math.sqrt" => nameof(Kernels.Runtime.CompiledRuntime.SqrtF64Raw),
            "math.floor" => nameof(Kernels.Runtime.CompiledRuntime.FloorF64Raw),
            "math.ceil" => nameof(Kernels.Runtime.CompiledRuntime.CeilF64Raw),
            "math.round" => nameof(Kernels.Runtime.CompiledRuntime.RoundF64Raw),
            _ => ""
        };

        return rawMethod.Length > 0 &&
           bindings.TryGet(call.Name, out var binding) &&
           binding.Compiled is { Kind: "RuntimeStub" } &&
           binding.Compiled.Type == typeof(Runtime.CompiledRuntime).FullName &&
           binding.Compiled.Method == BoxedMethod(call.Name) &&
           binding.Parameters.Count == 1 &&
           binding.Parameters[0].Equals(SandboxType.F64) &&
           binding.ReturnType.Equals(SandboxType.F64) &&
           binding.RequiredCapability is null &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           binding.AuditLevel == AuditLevel.None &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;
    }

    private static string BoxedMethod(string bindingId)
        => bindingId switch
        {
            "math.sqrt" => nameof(Kernels.Runtime.CompiledRuntime.SqrtF64),
            "math.floor" => nameof(Kernels.Runtime.CompiledRuntime.FloorF64),
            "math.ceil" => nameof(Kernels.Runtime.CompiledRuntime.CeilF64),
            "math.round" => nameof(Kernels.Runtime.CompiledRuntime.RoundF64),
            _ => ""
        };
}

internal static class I32MathIntrinsicEmitter
{
    public static bool TryEmit(
        Expression expression,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (expression is not CallExpression call ||
            !TryGetRawIntrinsic(call, bindings, out var rawMethod, out var argumentCount) ||
            call.Arguments.Count != argumentCount)
        {
            return false;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        var locals = new LocalBuilder[argumentCount];
        for (var i = 0; i < argumentCount; i++)
        {
            locals[i] = il.DeclareLocal(typeof(int));
            emitAs(call.Arguments[i], StackKind.I32);
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
        for (var i = 0; i < argumentCount; i++)
        {
            il.Emit(OpCodes.Ldloc, locals[i]);
        }

        il.Emit(OpCodes.Call, Runtime(rawMethod));
        return true;
    }

    private static bool TryGetRawIntrinsic(
        CallExpression call,
        IBindingCatalog bindings,
        out string rawMethod,
        out int argumentCount)
    {
        rawMethod = call.Name switch
        {
            "math.abs" => nameof(Kernels.Runtime.CompiledRuntime.AbsI32Raw),
            "math.min" => nameof(Kernels.Runtime.CompiledRuntime.MinI32Raw),
            "math.max" => nameof(Kernels.Runtime.CompiledRuntime.MaxI32Raw),
            "math.clamp" => nameof(Kernels.Runtime.CompiledRuntime.ClampI32Raw),
            _ => ""
        };
        argumentCount = call.Name switch
        {
            "math.abs" => 1,
            "math.min" or "math.max" => 2,
            "math.clamp" => 3,
            _ => 0
        };

        return rawMethod.Length > 0 &&
           bindings.TryGet(call.Name, out var binding) &&
           binding.Compiled is { Kind: "RuntimeStub" } &&
           binding.Compiled.Type == typeof(Runtime.CompiledRuntime).FullName &&
           binding.Compiled.Method == BoxedMethod(call.Name) &&
           binding.Parameters.Count == argumentCount &&
           ParametersAreI32(binding.Parameters) &&
           binding.ReturnType.Equals(SandboxType.I32) &&
           binding.RequiredCapability is null &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           binding.AuditLevel == AuditLevel.None &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;
    }

    private static bool ParametersAreI32(IReadOnlyList<SandboxType> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (!parameters[i].Equals(SandboxType.I32))
            {
                return false;
            }
        }

        return true;
    }

    private static string BoxedMethod(string bindingId)
        => bindingId switch
        {
            "math.abs" => nameof(Kernels.Runtime.CompiledRuntime.AbsI32),
            "math.min" => nameof(Kernels.Runtime.CompiledRuntime.MinI32),
            "math.max" => nameof(Kernels.Runtime.CompiledRuntime.MaxI32),
            "math.clamp" => nameof(Kernels.Runtime.CompiledRuntime.ClampI32),
            _ => ""
        };
}
