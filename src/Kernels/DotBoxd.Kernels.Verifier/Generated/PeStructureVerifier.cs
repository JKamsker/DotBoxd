namespace DotBoxd.Kernels.Verifier;

using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

internal static class PeStructureVerifier
{
    private static readonly HashSet<string> AllowedSectionNames = new(StringComparer.Ordinal) {
        ".text", ".rsrc", ".reloc"
    };

    public static void Verify(PEReader peReader, List<VerificationDiagnostic> diagnostics)
    {
        var headers = peReader.PEHeaders;
        if (!headers.IsDll) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-FORMAT", "generated artifact must be a DLL"));
        }

        var corHeader = headers.CorHeader;
        if (corHeader is null) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-METADATA", "assembly has no CLR header"));
            return;
        }

        if ((corHeader.Flags & CorFlags.ILOnly) == 0) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-MIXED", "mixed-mode or native code is not allowed"));
        }

        if ((corHeader.Flags & CorFlags.NativeEntryPoint) != 0 ||
            corHeader.EntryPointTokenOrRelativeVirtualAddress != 0) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-ENTRYPOINT", "generated artifacts must not define an entrypoint"));
        }

        if (HasDirectory(corHeader.CodeManagerTableDirectory) ||
            HasDirectory(corHeader.VtableFixupsDirectory) ||
            HasDirectory(corHeader.ExportAddressTableJumpsDirectory) ||
            HasDirectory(corHeader.ManagedNativeHeaderDirectory)) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-NATIVE", "native CLR header directories are not allowed"));
        }

        VerifyNativeDirectories(headers.PEHeader, diagnostics);
        VerifySectionHeaders(headers.SectionHeaders, diagnostics);
    }

    private static bool HasDirectory(DirectoryEntry directory)
        => directory.RelativeVirtualAddress != 0 || directory.Size != 0;

    private static void VerifyNativeDirectories(PEHeader? peHeader, List<VerificationDiagnostic> diagnostics)
    {
        if (peHeader is null) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-FORMAT", "assembly has no PE header"));
            return;
        }

        if (HasDirectory(peHeader.ExportTableDirectory) ||
            HasDirectory(peHeader.DelayImportTableDirectory) ||
            HasDirectory(peHeader.BoundImportTableDirectory) ||
            HasDirectory(peHeader.ThreadLocalStorageTableDirectory)) {
            diagnostics.Add(new VerificationDiagnostic("V-PE-NATIVE", "native PE directories are not allowed"));
        }
    }

    private static void VerifySectionHeaders(
        ImmutableArray<SectionHeader> sections,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var section in sections) {
            if (!AllowedSectionNames.Contains(section.Name)) {
                diagnostics.Add(new VerificationDiagnostic("V-PE-SECTION", $"PE section '{section.Name}' is not allowed"));
            }

            var characteristics = section.SectionCharacteristics;
            var executable = (characteristics & SectionCharacteristics.MemExecute) != 0;
            var writable = (characteristics & SectionCharacteristics.MemWrite) != 0;
            if ((executable && section.Name != ".text") || (executable && writable)) {
                diagnostics.Add(new VerificationDiagnostic("V-PE-SECTION", $"PE section '{section.Name}' has unsafe permissions"));
            }
        }
    }
}
