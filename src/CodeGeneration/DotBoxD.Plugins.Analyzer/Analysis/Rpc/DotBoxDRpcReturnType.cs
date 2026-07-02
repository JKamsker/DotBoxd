using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDRpcReturnType
{
    public static string JsonType(ITypeSymbol type, Compilation compilation)
    {
        var payloadType = PayloadType(type, compilation);
        return payloadType is null ? "\"Unit\"" : DotBoxDRpcTypeMapper.JsonType(payloadType, compilation);
    }

    public static ITypeSymbol? PayloadType(ITypeSymbol type, Compilation compilation)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return null;
        }

        if (DotBoxDWellKnownTaskTypes.IsTaskLike(type, compilation, out var taskLike))
        {
            return taskLike is { IsGenericType: true, TypeArguments.Length: 1 }
                ? taskLike.TypeArguments[0]
                : null;
        }

        return type;
    }

    public static bool IsTaskLike(ITypeSymbol type, Compilation compilation)
        => DotBoxDWellKnownTaskTypes.IsTaskLike(type, compilation);
}
