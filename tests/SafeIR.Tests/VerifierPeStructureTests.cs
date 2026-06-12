using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SafeIR.Tests;

public sealed class VerifierPeStructureTests
{
    private const int CorFlagsOffset = 16;
    private const int EntryPointOffset = 20;
    private const int CodeManagerDirectoryOffset = 40;
    private const int VtableFixupsDirectoryOffset = 48;
    private const int ExportAddressTableJumpsDirectoryOffset = 56;
    private const int ManagedNativeHeaderDirectoryOffset = 64;
    private const int SizeOfOptionalHeaderOffset = 16;
    private const int SectionNameSize = 8;
    private const int SectionCharacteristicsOffset = 36;
    private const int Pe32DataDirectoriesOffset = 96;
    private const int Pe32PlusDataDirectoriesOffset = 112;
    private const int Pe32PlusMagic = 0x20b;
    private const int ExportTableIndex = 0;
    private const int ThreadLocalStorageTableIndex = 9;
    private const int DelayImportTableIndex = 13;

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
            { WithManagedNativeHeaderDirectory, "V-PE-NATIVE" },
            { WithPeExportTableDirectory, "V-PE-NATIVE" },
            { WithPeDelayImportTableDirectory, "V-PE-NATIVE" },
            { WithPeTlsDirectory, "V-PE-NATIVE" },
            { WithSuspiciousSectionName, "V-PE-SECTION" },
            { WithWritableExecutableSection, "V-PE-SECTION" }
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

    private static byte[] WithPeExportTableDirectory()
        => MutatePeHeaderDirectory(ExportTableIndex);

    private static byte[] WithPeDelayImportTableDirectory()
        => MutatePeHeaderDirectory(DelayImportTableIndex);

    private static byte[] WithPeTlsDirectory()
        => MutatePeHeaderDirectory(ThreadLocalStorageTableIndex);

    private static byte[] WithSuspiciousSectionName()
        => MutateFirstSection((bytes, offset) => WriteSectionName(bytes, offset, ".edata"));

    private static byte[] WithWritableExecutableSection()
        => MutateFirstSection((bytes, offset) => OrUInt32(
            bytes,
            offset + SectionCharacteristicsOffset,
            (uint)SectionCharacteristics.MemWrite));

    private static byte[] MutateCorHeader(Action<byte[], int> mutate)
    {
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        mutate(bytes, reader.PEHeaders.CorHeaderStartOffset);
        return bytes;
    }

    private static byte[] MutatePeHeaderDirectory(int directoryIndex)
        => MutatePeHeaders((bytes, headers) => WriteDirectory(bytes, DataDirectoryOffset(bytes, headers, directoryIndex)));

    private static byte[] MutateFirstSection(Action<byte[], int> mutate)
        => MutatePeHeaders((bytes, headers) => mutate(bytes, FirstSectionHeaderOffset(bytes, headers)));

    private static byte[] MutatePeHeaders(Action<byte[], PEHeaders> mutate)
    {
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        mutate(bytes, reader.PEHeaders);
        return bytes;
    }

    private static int DataDirectoryOffset(byte[] bytes, PEHeaders headers, int directoryIndex)
    {
        var optionalHeaderOffset = OptionalHeaderOffset(headers);
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(optionalHeaderOffset, sizeof(ushort)));
        var dataDirectoriesOffset = magic == Pe32PlusMagic
            ? Pe32PlusDataDirectoriesOffset
            : Pe32DataDirectoriesOffset;
        return optionalHeaderOffset + dataDirectoriesOffset + directoryIndex * 2 * sizeof(int);
    }

    private static int FirstSectionHeaderOffset(byte[] bytes, PEHeaders headers)
    {
        var sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(headers.CoffHeaderStartOffset + SizeOfOptionalHeaderOffset, sizeof(ushort)));
        return OptionalHeaderOffset(headers) + sizeOfOptionalHeader;
    }

    private static int OptionalHeaderOffset(PEHeaders headers)
        => headers.PEHeaderStartOffset;

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

    private static void WriteSectionName(byte[] bytes, int offset, string name)
    {
        bytes.AsSpan(offset, SectionNameSize).Clear();
        Encoding.ASCII.GetBytes(name, bytes.AsSpan(offset, Math.Min(name.Length, SectionNameSize)));
    }
}
