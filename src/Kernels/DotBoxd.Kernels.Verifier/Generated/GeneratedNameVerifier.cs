namespace DotBoxd.Kernels.Verifier;

using System.Reflection.Metadata;

internal static class GeneratedNameVerifier
{
    private const string GeneratedNamespace = "DotBoxd.Kernels.Generated";
    private const string GeneratedTypePrefix = "Module_";
    private const string HelperMethodPrefix = "Fn_";

    public static void VerifyTypeName(
        MetadataReader reader,
        TypeDefinition type,
        List<VerificationDiagnostic> diagnostics)
    {
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        if (ns == GeneratedNamespace &&
            name.StartsWith(GeneratedTypePrefix, StringComparison.Ordinal) &&
            name.Length == GeneratedTypePrefix.Length + 16 &&
            name[GeneratedTypePrefix.Length..].All(IsLowerHex)) {
            return;
        }

        diagnostics.Add(new VerificationDiagnostic(
            "V-PUBLIC-SURFACE",
            "generated type name must match DotBoxd.Kernels.Generated.Module_<16-hex-hash>"));
    }

    public static bool IsAllowedMethodName(string name)
        => name == "Execute" ||
           (name.StartsWith(HelperMethodPrefix, StringComparison.Ordinal) &&
            name.Length > HelperMethodPrefix.Length &&
            name[HelperMethodPrefix.Length..].All(char.IsDigit));

    private static bool IsLowerHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
