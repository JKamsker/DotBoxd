using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class VerifierMeterShapeTests
{
    [Fact]
    public async Task Verifier_rejects_fuel_meter_amount_spoofed_by_unreachable_integer()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            var meter = fnIl.DefineLabel();
            EmitEnterCall(fnIl);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Ldc_I4_0);
            fnIl.Emit(OpCodes.Br_S, meter);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.MarkLabel(meter);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("positive meter amount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_runtime_work_metered_only_on_another_branch()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            var workPath = fnIl.DefineLabel();
            var end = fnIl.DefineLabel();
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Brtrue_S, workPath);
            EmitChargeFuel(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldc_I4_3);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Stloc, value);
            fnIl.Emit(OpCodes.Br_S, end);
            fnIl.MarkLabel(workPath);
            // Two metered work calls (Neg) on this branch, whose fuel was charged only on the other branch
            // -> rejected. I32 boxing is non-metered O(1), used only to produce the operand.
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.Neg))!);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.Neg))!);
            fnIl.Emit(OpCodes.Stloc, value);
            fnIl.MarkLabel(end);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("meter each runtime work call", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_type_construction_before_entering_call_meter()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            EmitTypeScalar(fnIl);
            fnIl.Emit(OpCodes.Pop);
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("before runtime work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_repeated_type_construction_behind_one_meter()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            EmitTypeScalar(fnIl);
            fnIl.Emit(OpCodes.Pop);
            EmitTypeScalar(fnIl);
            fnIl.Emit(OpCodes.Pop);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("meter each runtime work call", StringComparison.Ordinal));
    }

    private static MethodBuilder DefineFunction(TypeBuilder type)
        => type.DefineMethod(
            "Fn_0",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void EmitExecuteCalling(TypeBuilder type, MethodInfo function)
    {
        var il = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]).GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, function);
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

    private static void EmitTypeScalar(ILGenerator il)
    {
        il.Emit(OpCodes.Ldstr, "I32");
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.TypeScalar))!);
    }
}
