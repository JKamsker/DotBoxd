namespace SafeIR.Verifier;

using System.Reflection.PortableExecutable;

internal static class PeStructureVerifier
{
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
    }

    private static bool HasDirectory(DirectoryEntry directory)
        => directory.RelativeVirtualAddress != 0 || directory.Size != 0;
}
