namespace SafeIR.Compiler.Emitters;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static SafeIR.Compiler.IlEmitterPrimitives;

internal static class BindingCallEmitter
{
    public static bool TryEmit(
        CallExpression call,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression> emitExpression)
    {
        if (!bindings.TryGet(call.Name, out var binding) || !IsCompiledPureBinding(binding))
        {
            return false;
        }

        if (CanEmitDirectIntrinsic(binding))
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
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBindingCall)));
            foreach (var local in locals)
            {
                il.Emit(OpCodes.Ldloc, local);
            }

            il.Emit(OpCodes.Call, Runtime(binding.Compiled.Method));
            return true;
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        ValueArrayEmitter.Emit(il, call.Arguments, emitExpression);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CallBinding)));
        return true;
    }

    private static bool IsCompiledPureBinding(BindingSignature binding)
        => binding.Compiled.Kind == "RuntimeStub" &&
           binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
           binding.RequiredCapability is null &&
           binding.Safety is BindingSafety.PureHostFacade or BindingSafety.PureIntrinsic &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;

    private static bool CanEmitDirectIntrinsic(BindingSignature binding)
        => binding.Compiled.Method != nameof(CompiledRuntime.CallBinding) &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           binding.AuditLevel == AuditLevel.None;
}
