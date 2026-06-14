using System.Reflection;
using System.Reflection.Emit;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

public sealed class VerifierRuntimeMeteringTests
{
    [Fact]
    public async Task Verifier_rejects_execute_dispatch_before_entrypoint_validation()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineValidFunction(type);
            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Pop);
            EmitValidateInput(il);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("before dispatching", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_runtime_work_before_function_meters()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            il.Emit(OpCodes.Pop);
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitI32ReturnAfterExit(il, 2);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("before runtime work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_runtime_work_after_function_exit()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("after exiting", StringComparison.Ordinal));
    }

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static MethodBuilder DefineFunction(TypeBuilder type)
        => type.DefineMethod(
            "Fn_0",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext)]);

    private static MethodBuilder DefineValidFunction(TypeBuilder type)
    {
        var fn = DefineFunction(type);
        var il = fn.GetILGenerator();
        EmitEnterCall(il);
        EmitChargeFuel(il);
        EmitI32ReturnAfterExit(il, 1);
        return fn;
    }

    private static void EmitExecuteCalling(TypeBuilder type, MethodInfo function)
    {
        var il = DefineExecute(type).GetILGenerator();
        EmitValidateInput(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, function);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitValidateInput(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
    }

    private static void EmitI32ReturnAfterExit(ILGenerator il, int value)
    {
        var local = il.DeclareLocal(typeof(SandboxValue));
        il.Emit(OpCodes.Ldc_I4, value);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
        il.Emit(OpCodes.Stloc, local);
        EmitExitCall(il);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitEnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
    }
}
