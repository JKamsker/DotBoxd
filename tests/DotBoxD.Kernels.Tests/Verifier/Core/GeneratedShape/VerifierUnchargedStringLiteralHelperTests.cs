using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core.GeneratedShape;

public sealed class VerifierUnchargedStringLiteralHelperTests
{
    [Fact]
    public async Task Verifier_rejects_uncharged_string_literal_helper()
    {
        var result = await VerifierTestHelpers.VerifyAsync(UnchargedStringLiteralHelperAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            d.Message.Contains(nameof(CompiledRuntime.StringLiteralValue), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_uncharged_string_literal_when_branch_skips_charge()
    {
        var result = await VerifierTestHelpers.VerifyAsync(BranchSkipsStringLiteralChargeAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            d.Message.Contains(nameof(CompiledRuntime.StringLiteralValue), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_zero_count_bulk_charge_for_string_literal_helper()
    {
        var result = await VerifierTestHelpers.VerifyAsync(ZeroCountBulkChargeStringLiteralAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            (d.Message.Contains(nameof(CompiledRuntime.StringLiteralValue), StringComparison.Ordinal) ||
                d.Message.Contains(nameof(CompiledRuntime.ChargeSandboxValues), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Verifier_rejects_uncharged_guid_literal_helper()
    {
        var result = await VerifierTestHelpers.VerifyAsync(UnchargedGuidLiteralHelperAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            d.Message.Contains(nameof(CompiledRuntime.GuidLiteralValue), StringComparison.Ordinal));
    }

    private static byte[] UnchargedStringLiteralHelperAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldstr, "hello");
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue), [typeof(string)])!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var il = DefineExecute(type).GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] BranchSkipsStringLiteralChargeAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            var ret = fnIl.DefineLabel();
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldstr, "hello");
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue), [typeof(string)])!);
            fnIl.Emit(OpCodes.Stloc, value);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Brtrue_S, ret);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(
                    nameof(CompiledRuntime.ChargeSandboxValue),
                    [typeof(SandboxContext), typeof(SandboxValue)])!);
            EmitExitCall(fnIl);
            fnIl.MarkLabel(ret);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var il = DefineExecute(type).GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] ZeroCountBulkChargeStringLiteralAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldstr, "hello");
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue), [typeof(string)])!);
            fnIl.Emit(OpCodes.Stloc, value);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ldc_I4_0);
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(
                    nameof(CompiledRuntime.ChargeSandboxValues),
                    [typeof(SandboxContext), typeof(SandboxValue), typeof(int)])!);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var il = DefineExecute(type).GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] UnchargedGuidLiteralHelperAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldstr, "00112233-4455-6677-8899-aabbccddeeff");
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.GuidLiteralValue), [typeof(string)])!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var il = DefineExecute(type).GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

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
