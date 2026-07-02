using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class BindingCallEmitter
{
    public static bool TryEmit(
        CallExpression call,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression> emitExpression)
    {
        if (!bindings.TryGet(call.Name, out var binding) || !CanEmitCompiledBinding(binding))
        {
            return false;
        }

        if (CanEmitDirectRuntimeMethod(binding))
        {
            var locals = new LocalBuilder[call.Arguments.Count];
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                emitExpression(call.Arguments[i]);
                locals[i] = il.DeclareLocal(typeof(SandboxValue));
                il.Emit(OpCodes.Stloc, locals[i]);
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, call.Name);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
            if (DirectRuntimeMethodRequiresContext(binding.Compiled.Method))
            {
                il.Emit(OpCodes.Ldarg_0);
            }

            foreach (var local in locals)
            {
                il.Emit(OpCodes.Ldloc, local);
            }

            il.Emit(OpCodes.Call, Runtime(binding.Compiled.Method));
            return true;
        }

        if (call.Arguments.Count == 1)
        {
            EmitOneArgumentGenericCall(call, il, emitExpression);
            return true;
        }

        if (call.Arguments.Count == 2)
        {
            EmitTwoArgumentGenericCall(call, il, emitExpression);
            return true;
        }

        EmitArrayBackedGenericCall(call, il, emitExpression);
        return true;
    }

    private static void EmitOneArgumentGenericCall(
        CallExpression call,
        ILGenerator il,
        Action<Expression> emitExpression)
    {
        var arg0 = il.DeclareLocal(typeof(SandboxValue));
        emitExpression(call.Arguments[0]);
        il.Emit(OpCodes.Stloc, arg0);

        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, call.Arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeValueArray)));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Ldloc, arg0);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CallBinding1)));
    }

    private static void EmitTwoArgumentGenericCall(
        CallExpression call,
        ILGenerator il,
        Action<Expression> emitExpression)
    {
        // Evaluate the arguments before the synthetic ChargeValueArray charge. An argument may itself
        // be a side-effecting binding call (now compilable for descriptor-governed stubs), and the
        // interpreter evaluates every argument expression before charging the binding call. Charging
        // the array first would let a tight fuel/allocation budget throw QuotaExceeded before a
        // side-effecting argument runs, so the compiled run would skip an effect the interpreter
        // performs. Materializing the arguments into locals first preserves that ordering.
        var arg0 = il.DeclareLocal(typeof(SandboxValue));
        emitExpression(call.Arguments[0]);
        il.Emit(OpCodes.Stloc, arg0);
        var arg1 = il.DeclareLocal(typeof(SandboxValue));
        emitExpression(call.Arguments[1]);
        il.Emit(OpCodes.Stloc, arg1);

        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, call.Arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeValueArray)));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Ldloc, arg0);
        il.Emit(OpCodes.Ldloc, arg1);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CallBinding2)));
    }

    private static void EmitArrayBackedGenericCall(
        CallExpression call,
        ILGenerator il,
        Action<Expression> emitExpression)
    {
        ValueArrayEmitter.Emit(il, call.Arguments, emitExpression);
        var args = il.DeclareLocal(typeof(SandboxValue[]));
        il.Emit(OpCodes.Stloc, args);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));
    }

    private static bool CanEmitCompiledBinding(BindingSignature binding)
        => CanEmitGenericRuntimeStub(binding) || CanEmitDirectRuntimeMethod(binding);

    // SECURITY-SENSITIVE GATE. This admits ANY binding whose compiled descriptor is a CompiledRuntime
    // "RuntimeStub" pointing at CallBinding — regardless of its RequiredCapability, Safety, Effects, or
    // AuditLevel. There is deliberately NO compile-time capability/effects/audit check here: a binding
    // routed through CallBinding is dispatched at runtime by CompiledBindingDispatcher.CallBinding /
    // CallBinding2, which perform the SAME capability check (ChargeBindingCall), quota and return
    // charging, and success/failure audit as the interpreter. That runtime dispatch is therefore the
    // SOLE gate for compiled side-effecting bindings (verified by the differential parity suite under
    // tests/.../Compiled/SideEffectParity). Consequence: binding REGISTRATION is security-sensitive —
    // giving a descriptor a CallBinding stub makes it compilable with no compile-time fence, so the
    // descriptor's capability/effects/audit metadata must be correct and is what gets enforced. The
    // pure direct-runtime path below stays restricted to capability-free PureIntrinsic Cpu/Alloc
    // methods. See PR #27 and #32 (binding-registration safety note).
    private static bool CanEmitGenericRuntimeStub(BindingSignature binding)
        => binding.Compiled.Kind == "RuntimeStub" &&
           binding.Compiled.Type == typeof(Runtime.CompiledRuntime).FullName &&
           binding.Compiled.Method == nameof(Kernels.Runtime.CompiledRuntime.CallBinding);

    private static bool CanEmitDirectRuntimeMethod(BindingSignature binding)
        => binding.Compiled.Kind == "RuntimeStub" &&
           binding.Compiled.Type == typeof(Runtime.CompiledRuntime).FullName &&
           binding.Compiled.Method != nameof(Kernels.Runtime.CompiledRuntime.CallBinding) &&
           !binding.IsAsync &&
           binding.RequiredCapability is null &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None &&
           binding.AuditLevel == AuditLevel.None;

    private static bool DirectRuntimeMethodRequiresContext(string method)
        => method is nameof(Kernels.Runtime.CompiledRuntime.ConcatString)
            or nameof(Kernels.Runtime.CompiledRuntime.Int32ToStringInvariant);
}
