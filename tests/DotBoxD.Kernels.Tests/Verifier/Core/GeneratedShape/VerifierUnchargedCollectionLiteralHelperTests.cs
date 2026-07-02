using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core.GeneratedShape;

public sealed class VerifierUnchargedCollectionLiteralHelperTests
{
    [Fact]
    public async Task Verifier_rejects_uncharged_collection_literal_helper()
    {
        var result = await VerifierTestHelpers.VerifyAsync(UnchargedListLiteralHelperAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            (d.Message.Contains(nameof(CompiledRuntime.CreateLiteralValueArray), StringComparison.Ordinal) ||
                d.Message.Contains(nameof(CompiledRuntime.ListLiteralValue), StringComparison.Ordinal)));
    }

    private static byte[] UnchargedListLiteralHelperAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var values = fnIl.DeclareLocal(typeof(SandboxValue[]));
            var list = fnIl.DeclareLocal(typeof(SandboxValue));

            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.CreateLiteralValueArray))!);
            fnIl.Emit(OpCodes.Stloc, values);
            fnIl.Emit(OpCodes.Ldloc, values);
            fnIl.Emit(OpCodes.Ldc_I4_0);
            fnIl.Emit(OpCodes.Ldc_I4_S, 42);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Stelem_Ref);
            EmitTypeScalar(fnIl);
            fnIl.Emit(OpCodes.Ldloc, values);
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ListLiteralValue))!);
            fnIl.Emit(OpCodes.Stloc, list);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, list);
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

    private static void EmitTypeScalar(ILGenerator il)
    {
        il.Emit(OpCodes.Ldstr, "I32");
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.TypeScalar))!);
    }
}
