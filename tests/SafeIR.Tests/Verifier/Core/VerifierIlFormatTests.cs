using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SafeIR.Tests;

public sealed class VerifierIlFormatTests
{
    [Fact]
    public async Task Verifier_reports_truncated_instruction_operand_as_il_format()
    {
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });
        var ilBytes = ReadExecuteIl(bytes);
        var ilStart = IndexOf(bytes, ilBytes);
        bytes[ilStart] = 0x20;

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-IL-FORMAT");
    }

    [Fact]
    public async Task Verifier_reports_oversized_switch_table_as_il_format()
    {
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = method.GetILGenerator();
            for (var i = 0; i < 8; i++)
            {
                il.Emit(OpCodes.Nop);
            }

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });
        var ilBytes = ReadExecuteIl(bytes);
        var ilStart = IndexOf(bytes, ilBytes);
        bytes[ilStart] = 0x45;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(ilStart + 1, sizeof(int)), int.MaxValue);

        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-IL-FORMAT");
    }

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
