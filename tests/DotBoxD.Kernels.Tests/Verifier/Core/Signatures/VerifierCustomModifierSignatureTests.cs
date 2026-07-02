using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core.Signatures;

public sealed class VerifierCustomModifierSignatureTests
{
    [Theory]
    [InlineData("Execute SandboxContext parameter", "V-EXECUTE-SIGNATURE")]
    [InlineData("Fn_1 SandboxValue return", "V-FUNCTION-SIGNATURE")]
    public async Task Verifier_rejects_custom_modified_generated_method_signatures(
        string shape,
        string expectedCode)
    {
        var assembly = shape.StartsWith("Execute", StringComparison.Ordinal)
            ? AssemblyWithModifiedExecuteParameter()
            : AssemblyWithModifiedFunctionReturn();

        var result = await VerifierTestHelpers.VerifyAsync(assembly);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
    }

    private static byte[] AssemblyWithModifiedExecuteParameter()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var function = DefineValidFunction(type, "Fn_0");
            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(SandboxValue),
                returnTypeRequiredCustomModifiers: null,
                returnTypeOptionalCustomModifiers: null,
                parameterTypes: [typeof(SandboxContext), typeof(SandboxValue)],
                parameterTypeRequiredCustomModifiers: [[typeof(SandboxValue)], Type.EmptyTypes],
                parameterTypeOptionalCustomModifiers: null);

            EmitExecuteBody(execute, function);
        });

    private static byte[] AssemblyWithModifiedFunctionReturn()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var function = type.DefineMethod(
                "Fn_1",
                MethodAttributes.Private | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(SandboxValue),
                returnTypeRequiredCustomModifiers: [typeof(SandboxValue)],
                returnTypeOptionalCustomModifiers: null,
                parameterTypes: [typeof(SandboxContext)],
                parameterTypeRequiredCustomModifiers: null,
                parameterTypeOptionalCustomModifiers: null);
            EmitFunctionBody(function);

            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            EmitExecuteBody(execute, function);
        });

    private static MethodBuilder DefineValidFunction(TypeBuilder type, string name)
    {
        var function = type.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext)]);
        EmitFunctionBody(function);
        return function;
    }

    private static void EmitExecuteBody(MethodBuilder execute, MethodInfo function)
    {
        var il = execute.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, function);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitFunctionBody(MethodBuilder function)
    {
        var il = function.GetILGenerator();
        var value = il.DeclareLocal(typeof(SandboxValue));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);
    }
}
