using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Verifier.Generated;

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
            d.Message.Contains("DotBoxD.Kernels.Sandbox.SandboxValue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_local_store_type_mismatch()
    {
        var result = await VerifierTestHelpers.VerifyAsync(LocalStoreMismatchAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("System.Int32", StringComparison.Ordinal) &&
            d.Message.Contains("DotBoxD.Kernels.Sandbox.SandboxValue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Verifier_accepts_consistent_branch_stack_snapshots(int depth)
    {
        var result = await VerifierTestHelpers.VerifyAsync(BranchMergeAssembly(depth, consistent: true));

        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("inconsistent stack types", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Verifier_rejects_inconsistent_branch_stack_snapshots(int depth)
    {
        var result = await VerifierTestHelpers.VerifyAsync(BranchMergeAssembly(depth, consistent: false));

        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("inconsistent stack types", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("i32-to-i64")]
    [InlineData("i32-to-f64")]
    [InlineData("i64-to-f64")]
    public async Task Verifier_allows_generated_numeric_conversion_opcodes(string conversion)
    {
        var result = await VerifierTestHelpers.VerifyAsync(NumericConversionAssembly(conversion));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "V-OPCODE");
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("Conv_", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("f64-to-i64")]
    [InlineData("value-to-f64")]
    public async Task Verifier_rejects_invalid_numeric_conversion_operands(string conversion)
    {
        var result = await VerifierTestHelpers.VerifyAsync(NumericConversionAssembly(conversion));

        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-STACK-TYPE" &&
            d.Message.Contains("Conv_", StringComparison.OrdinalIgnoreCase));
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
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.I32))!);
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

    private static byte[] BranchMergeAssembly(int depth, bool consistent)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var il = DefineExecute(type).GetILGenerator();
            var right = il.DefineLabel();
            var join = il.DefineLabel();

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Brtrue_S, right);
            EmitStack(il, depth, inconsistent: false);
            il.Emit(OpCodes.Br_S, join);
            il.MarkLabel(right);
            EmitStack(il, depth, inconsistent: !consistent);
            il.MarkLabel(join);
            for (var i = 0; i < depth; i++)
            {
                il.Emit(OpCodes.Pop);
            }

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] NumericConversionAssembly(string conversion)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var il = DefineExecute(type).GetILGenerator();
            switch (conversion)
            {
                case "i32-to-i64":
                    il.Emit(OpCodes.Ldc_I4, 123);
                    il.Emit(OpCodes.Conv_I8);
                    il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.I64))!);
                    break;
                case "i32-to-f64":
                    il.Emit(OpCodes.Ldc_I4, 123);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.F64))!);
                    break;
                case "i64-to-f64":
                    il.Emit(OpCodes.Ldc_I8, 456L);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.F64))!);
                    break;
                case "f64-to-i64":
                    il.Emit(OpCodes.Ldc_R8, 1.25);
                    il.Emit(OpCodes.Conv_I8);
                    il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.I64))!);
                    break;
                case "value-to-f64":
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(Kernels.Runtime.CompiledRuntime.F64))!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(conversion), conversion, null);
            }

            il.Emit(OpCodes.Ret);
        });

    private static void EmitStack(ILGenerator il, int depth, bool inconsistent)
    {
        for (var i = 0; i < depth; i++)
        {
            if (inconsistent && i == depth - 1)
            {
                il.Emit(OpCodes.Ldc_I4_0);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_1);
            }
        }
    }

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
}
