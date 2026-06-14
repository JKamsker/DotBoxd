using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class VerifierStackTypeTests
{
    [Theory]
    [InlineData("ldarg")]
    [InlineData("ldloc")]
    [InlineData("starg")]
    [InlineData("stloc")]
    public async Task Verifier_rejects_out_of_range_argument_and_local_operands(string opcode)
    {
        var result = await VerifierTestHelpers.VerifyAsync(OperandOutOfRangeAssembly(opcode));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-OPERAND");
    }

    [Fact]
    public async Task Verifier_rejects_call_argument_type_mismatch()
    {
        var result = await VerifierTestHelpers.VerifyAsync(CallArgumentMismatchAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("System.Int32", StringComparison.Ordinal) &&
            d.Message.Contains("SafeIR.SandboxValue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_local_store_type_mismatch()
    {
        var result = await VerifierTestHelpers.VerifyAsync(LocalStoreMismatchAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("System.Int32", StringComparison.Ordinal) &&
            d.Message.Contains("SafeIR.SandboxValue", StringComparison.Ordinal));
    }

    private static byte[] OperandOutOfRangeAssembly(string opcode)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var il = DefineExecute(type).GetILGenerator();
            switch (opcode)
            {
                case "ldarg":
                    il.Emit(OpCodes.Ldarg_S, (byte)3);
                    il.Emit(OpCodes.Ret);
                    break;
                case "ldloc":
                    il.Emit(OpCodes.Ldloc_S, (byte)0);
                    il.Emit(OpCodes.Ret);
                    break;
                case "starg":
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Starg_S, (byte)3);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ret);
                    break;
                case "stloc":
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stloc_S, (byte)0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ret);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(opcode), opcode, null);
            }
        });

    private static byte[] CallArgumentMismatchAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var il = DefineExecute(type).GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] LocalStoreMismatchAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var il = DefineExecute(type).GetILGenerator();
            var local = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
}
