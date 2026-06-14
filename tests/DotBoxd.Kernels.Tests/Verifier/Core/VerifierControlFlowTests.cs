using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotBoxd.Kernels.Tests;

public sealed class VerifierControlFlowTests
{
    [Fact]
    public async Task Verifier_rejects_branch_to_non_instruction_offset()
    {
        var result = await VerifierTestHelpers.VerifyAsync(InvalidBranchTargetAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-CONTROL-FLOW");
    }

    [Fact]
    public async Task Verifier_rejects_operand_stack_underflow()
    {
        var result = await VerifierTestHelpers.VerifyAsync(StackUnderflowAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-STACK");
    }

    [Fact]
    public async Task Verifier_rejects_inconsistent_branch_stack_height()
    {
        var result = await VerifierTestHelpers.VerifyAsync(InconsistentBranchStackAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-STACK");
    }

    private static byte[] InvalidBranchTargetAssembly()
    {
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = method.GetILGenerator();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Br_S, done);
            il.Emit(OpCodes.Ldc_I4, 123_456);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });

        var ilBytes = ReadExecuteIl(bytes);
        var ilStart = IndexOf(bytes, ilBytes);
        bytes[ilStart + 1] = 2;
        return bytes;
    }

    private static byte[] StackUnderflowAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] InconsistentBranchStackAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            var join = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Brtrue_S, join);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(join);
            il.Emit(OpCodes.Ret);
        });

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static byte[] ReadExecuteIl(byte[] assemblyBytes)
    {
        using var stream = new MemoryStream(assemblyBytes, writable: false);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == "Execute")
                {
                    return peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ??
                        throw new InvalidOperationException("Execute method has no IL");
                }
            }
        }

        throw new InvalidOperationException("Execute method not found");
    }

    private static int IndexOf(byte[] bytes, byte[] pattern)
    {
        for (var i = 0; i <= bytes.Length - pattern.Length; i++)
        {
            if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        throw new InvalidOperationException("IL pattern not found");
    }
}
