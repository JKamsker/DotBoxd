namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis;

internal static class DotBoxdTypeNameReader
{
    public static string SandboxTypeName(ITypeSymbol type)
        => type.SpecialType switch {
            SpecialType.System_Boolean => DotBoxdGenerationNames.ManifestTypes.Bool,
            SpecialType.System_Int32 => DotBoxdGenerationNames.ManifestTypes.Int,
            SpecialType.System_Int64 => DotBoxdGenerationNames.ManifestTypes.Long,
            SpecialType.System_Double => DotBoxdGenerationNames.ManifestTypes.Double,
            SpecialType.System_String => DotBoxdGenerationNames.ManifestTypes.String,
            _ => DotBoxdGenerationNames.ManifestTypes.Unsupported
        };

    public static bool IsSupportedScalar(ITypeSymbol type)
        => !string.Equals(
            SandboxTypeName(type),
            DotBoxdGenerationNames.ManifestTypes.Unsupported,
            StringComparison.Ordinal);
}
