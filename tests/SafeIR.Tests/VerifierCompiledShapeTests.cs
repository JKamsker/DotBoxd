using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class VerifierCompiledShapeTests
{
    [Fact]
    public async Task Verifier_rejects_execute_without_entrypoint_validation()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    [Fact]
    public async Task Verifier_rejects_generated_function_without_meters()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var fnIl = fn.GetILGenerator();
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(OpCodes.Ret);

            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    [Fact]
    public async Task Verifier_rejects_execute_with_unreachable_entrypoint_validation()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
            EmitValidateInput(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    [Fact]
    public async Task Verifier_rejects_execute_when_branch_skips_entrypoint_validation()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            var skipValidation = il.DefineLabel();
            il.Emit(OpCodes.Br_S, skipValidation);
            EmitValidateInput(il);
            il.MarkLabel(skipValidation);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    [Fact]
    public async Task Verifier_rejects_generated_function_with_unreachable_meters()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(OpCodes.Ret);
            EmitFunctionMeters(fnIl);
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    [Fact]
    public async Task Verifier_rejects_generated_function_when_branch_skips_exit()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            var skipExit = fnIl.DefineLabel();
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Brtrue_S, skipExit);
            EmitExitCall(fnIl);
            fnIl.MarkLabel(skipExit);
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
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
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void EmitExecuteCalling(TypeBuilder type, MethodInfo function)
    {
        var il = DefineExecute(type).GetILGenerator();
        EmitValidateInput(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, function);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitValidateInput(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
    }

    private static void EmitFunctionMeters(ILGenerator il)
    {
        EmitEnterCall(il);
        EmitChargeFuel(il);
        EmitExitCall(il);
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
