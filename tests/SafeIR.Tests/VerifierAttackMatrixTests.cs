using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierAttackMatrixTests
{
    public static TheoryData<string, Func<byte[]>, string[]> MissingAttackCases()
        => new() {
            { "System.Net.Http.HttpClient", HttpClientAssembly, ["V-TYPE-FORBIDDEN", "V-ASM-REF"] },
            { "System.Diagnostics.Process.Start", ProcessStartAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "System.Threading.Tasks.Task.Run", TaskRunAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "calli opcode", CalliAssembly, ["V-OPCODE"] },
            { "ldftn opcode", LdftnAssembly, ["V-OPCODE"] },
            { "raw SandboxValue array allocation", RawSandboxValueArrayAssembly, ["V-OPCODE"] }
        };

    [Theory]
    [MemberData(nameof(MissingAttackCases))]
    public async Task Verifier_rejects_documented_attack_matrix_cases(
        string name,
        Func<byte[]> build,
        string[] expectedCodes)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => expectedCodes.Contains(d.Code));
        Assert.NotEmpty(name);
    }

    private static byte[] HttpClientAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Newobj, typeof(HttpClient).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] ProcessStartAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "cmd.exe");
            il.Emit(OpCodes.Call, typeof(Process).GetMethod(nameof(Process.Start), [typeof(string)])!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] TaskRunAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Task).GetMethod(nameof(Task.Run), [typeof(Action)])!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] CalliAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_0);
            il.EmitCalli(OpCodes.Calli, CallingConvention.StdCall, typeof(void), Type.EmptyTypes);
            ReturnInput(il);
        });

    private static byte[] LdftnAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var helper = type.DefineMethod("Fn_1", MethodAttributes.Private | MethodAttributes.Static, typeof(void), []);
            helper.GetILGenerator().Emit(OpCodes.Ret);
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldftn, helper);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] RawSandboxValueArrayAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, typeof(SandboxValue));
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void ReturnInput(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }
}
