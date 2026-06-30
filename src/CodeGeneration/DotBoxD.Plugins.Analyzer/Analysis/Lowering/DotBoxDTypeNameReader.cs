using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDTypeNameReader
{
    public static string SandboxTypeName(ITypeSymbol type)
        => UnwrapTaskLike(type).SpecialType switch
        {
            SpecialType.System_Boolean => DotBoxDGenerationNames.ManifestTypes.Bool,
            SpecialType.System_Int32 => DotBoxDGenerationNames.ManifestTypes.Int,
            SpecialType.System_Int64 => DotBoxDGenerationNames.ManifestTypes.Long,
            SpecialType.System_Double => DotBoxDGenerationNames.ManifestTypes.Double,
            SpecialType.System_String => DotBoxDGenerationNames.ManifestTypes.String,
            _ => DotBoxDGenerationNames.ManifestTypes.Unsupported
        };

    public static string LiveSettingTypeName(ITypeSymbol type)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => DotBoxDGenerationNames.ManifestTypes.Bool,
            SpecialType.System_Int32 => DotBoxDGenerationNames.ManifestTypes.Int,
            SpecialType.System_Int64 => DotBoxDGenerationNames.ManifestTypes.Long,
            SpecialType.System_Double => DotBoxDGenerationNames.ManifestTypes.Double,
            SpecialType.System_String => DotBoxDGenerationNames.ManifestTypes.String,
            _ => DotBoxDGenerationNames.ManifestTypes.Unsupported
        };

    public static bool IsSupportedScalar(ITypeSymbol type)
        => !string.Equals(
            SandboxTypeName(type),
            DotBoxDGenerationNames.ManifestTypes.Unsupported,
            StringComparison.Ordinal);

    public static bool IsSupportedLiveSettingType(ITypeSymbol type)
        => !string.Equals(
            LiveSettingTypeName(type),
            DotBoxDGenerationNames.ManifestTypes.Unsupported,
            StringComparison.Ordinal);

    public static string KernelMethodTypeName(ITypeSymbol type)
        => SandboxTypeSourceEmitter.ManifestTag(UnwrapTaskLike(type));

    public static ITypeSymbol UnwrapTaskLike(ITypeSymbol type)
        => TryUnwrapTaskLike(type, out var inner) ? inner : type;

    public static bool TryUnwrapTaskLike(ITypeSymbol type, out ITypeSymbol inner)
    {
        if (type is INamedTypeSymbol
            {
                IsGenericType: true,
                TypeArguments.Length: 1,
                Name: "Task" or "ValueTask",
                ContainingNamespace: { } ns
            } named &&
            string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
        {
            inner = named.TypeArguments[0];
            return true;
        }

        inner = type;
        return false;
    }
}
