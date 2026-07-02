using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Verifier.Generated;

public sealed class VerifierShapeHardeningTests
{
    [Fact]
    public async Task Verifier_rejects_execute_that_discards_generated_function_result()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            EmitFunctionMeters(fnIl);
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(OpCodes.Ret);

            var il = DefineExecute(type).GetILGenerator();
            EmitValidateInput(il);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("directly return", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_meter_call_reached_from_zero_amount_branch()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var zero = il.DefineLabel();
            var positive = il.DefineLabel();
            var meter = il.DefineLabel();
            EmitEnterCall(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brtrue_S, zero);
            il.Emit(OpCodes.Br_S, positive);
            il.MarkLabel(zero);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Br_S, meter);
            il.MarkLabel(positive);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.MarkLabel(meter);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel))!);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("positive meter amount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_long_instruction_sequence_after_one_meter()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            for (var i = 0; i < 40; i++)
            {
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Pop);
            }

            EmitExitCall(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("long instruction sequences", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_null_sandbox_type_record_fields()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var typeArray = il.DeclareLocal(typeof(SandboxType[]));
            var resultValue = il.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(il);
            EmitChargeFuel(il);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.CreateTypeArray)));
            il.Emit(OpCodes.Stloc, typeArray);
            il.Emit(OpCodes.Ldloc, typeArray);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stelem_Ref);
            EmitChargeFuel(il);
            il.Emit(OpCodes.Ldloc, typeArray);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeRecord)));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
            il.Emit(OpCodes.Stloc, resultValue);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldloc, resultValue);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("SandboxType", StringComparison.Ordinal) &&
            d.Message.Contains("null", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_forbidden_sandbox_type_scalar_names()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var resultValue = il.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(il);
            EmitChargeFuel(il);
            il.Emit(OpCodes.Ldstr, "System.String");
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeScalar)));
            EmitChargeFuel(il);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeList)));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
            il.Emit(OpCodes.Stloc, resultValue);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldloc, resultValue);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-STACK-TYPE") &&
            d.Message.Contains("SandboxType", StringComparison.Ordinal) &&
            (d.Message.Contains("System.String", StringComparison.Ordinal) ||
                d.Message.Contains(nameof(CompiledRuntime.TypeScalar), StringComparison.Ordinal)) &&
            (d.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                d.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase)));
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
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.ValidateEntrypointInput))!);
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
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.EnterCall))!);
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel))!);
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.ExitCall))!);
    }

    private static MethodInfo RuntimeMethod(string name)
        => typeof(CompiledRuntime).GetMethod(name) ?? throw new MissingMethodException(nameof(CompiledRuntime), name);
}
