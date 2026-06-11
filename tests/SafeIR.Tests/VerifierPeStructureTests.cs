using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace SafeIR.Tests;

public sealed class VerifierPeStructureTests
{
    private const int CorFlagsOffset = 16;
    private const int EntryPointOffset = 20;
    private const int CodeManagerDirectoryOffset = 40;
    private const int VtableFixupsDirectoryOffset = 48;
    private const int ExportAddressTableJumpsDirectoryOffset = 56;
    private const int ManagedNativeHeaderDirectoryOffset = 64;

    [Theory]
    [MemberData(nameof(MutatedPeAssemblies))]
    public async Task Verifier_rejects_forbidden_pe_structure(Func<byte[]> build, string expectedCode)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
    }

    public static TheoryData<Func<byte[]>, string> MutatedPeAssemblies()
        => new() {
            { WithoutIlOnlyFlag, "V-PE-MIXED" },
            { WithManagedEntrypoint, "V-PE-ENTRYPOINT" },
            { WithNativeEntrypointFlag, "V-PE-ENTRYPOINT" },
            { WithCodeManagerDirectory, "V-PE-NATIVE" },
            { WithVtableFixupsDirectory, "V-PE-NATIVE" },
            { WithExportAddressTableJumpsDirectory, "V-PE-NATIVE" },
            { WithManagedNativeHeaderDirectory, "V-PE-NATIVE" }
        };

    private static byte[] WithoutIlOnlyFlag()
        => MutateCorHeader((bytes, offset) => AndUInt32(bytes, offset + CorFlagsOffset, ~(uint)CorFlags.ILOnly));

    private static byte[] WithManagedEntrypoint()
        => MutateCorHeader((bytes, offset) => WriteInt32(bytes, offset + EntryPointOffset, 1));

    private static byte[] WithNativeEntrypointFlag()
        => MutateCorHeader((bytes, offset) => OrUInt32(bytes, offset + CorFlagsOffset, (uint)CorFlags.NativeEntryPoint));

    private static byte[] WithCodeManagerDirectory()
        => MutateCorHeader((bytes, offset) => WriteDirectory(bytes, offset + CodeManagerDirectoryOffset));

    private static byte[] WithVtableFixupsDirectory()
        => MutateCorHeader((bytes, offset) => WriteDirectory(bytes, offset + VtableFixupsDirectoryOffset));

    private static byte[] WithExportAddressTableJumpsDirectory()
        => MutateCorHeader((bytes, offset) => WriteDirectory(bytes, offset + ExportAddressTableJumpsDirectoryOffset));

    private static byte[] WithManagedNativeHeaderDirectory()
        => MutateCorHeader((bytes, offset) => WriteDirectory(bytes, offset + ManagedNativeHeaderDirectoryOffset));

    private static byte[] MutateCorHeader(Action<byte[], int> mutate)
    {
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        mutate(bytes, reader.PEHeaders.CorHeaderStartOffset);
        return bytes;
    }

    private static void OrUInt32(byte[] bytes, int offset, uint mask)
        => WriteUInt32(bytes, offset, ReadUInt32(bytes, offset) | mask);

    private static void AndUInt32(byte[] bytes, int offset, uint mask)
        => WriteUInt32(bytes, offset, ReadUInt32(bytes, offset) & mask);

    private static uint ReadUInt32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

    private static void WriteInt32(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static void WriteDirectory(byte[] bytes, int offset)
    {
        WriteInt32(bytes, offset, 1);
        WriteInt32(bytes, offset + sizeof(int), 1);
    }
}
