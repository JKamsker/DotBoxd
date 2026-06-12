namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal static class SafeIrTypeNameReader
{
    public static string SandboxTypeName(ITypeSymbol type)
        => type.SpecialType switch {
            SpecialType.System_Boolean => SafeIrGenerationNames.ManifestTypes.Bool,
            SpecialType.System_Int32 => SafeIrGenerationNames.ManifestTypes.Int,
            SpecialType.System_Int64 => SafeIrGenerationNames.ManifestTypes.Long,
            SpecialType.System_Double => SafeIrGenerationNames.ManifestTypes.Double,
            SpecialType.System_String => SafeIrGenerationNames.ManifestTypes.String,
            _ => SafeIrGenerationNames.ManifestTypes.Unsupported
        };

    public static bool IsSupportedScalar(ITypeSymbol type)
        => !string.Equals(
            SandboxTypeName(type),
            SafeIrGenerationNames.ManifestTypes.Unsupported,
            StringComparison.Ordinal);
}
