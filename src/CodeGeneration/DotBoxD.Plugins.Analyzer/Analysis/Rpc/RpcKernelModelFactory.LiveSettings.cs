using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using static DotBoxDRpcJsonLowerer;

internal static partial class RpcKernelModelFactory
{
    private static void ValidateGeneratedParameterNames(
        IMethodSymbol method,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < method.Parameters.Length - 1; i++)
        {
            parameterNames.Add(method.Parameters[i].Name);
        }

        foreach (var setting in liveSettings)
        {
            if (parameterNames.Contains(setting.Name))
            {
                throw new NotSupportedException(
                    $"Live setting '{setting.Name}' conflicts with a kernel RPC parameter.");
            }
        }
    }

    private static string JoinLiveSettings(EquatableArray<LiveSettingModel> liveSettings)
    {
        var parts = new List<string>(liveSettings.Count);
        foreach (var setting in liveSettings)
        {
            parts.Add(LiveSettingJson(setting));
        }

        return string.Join(",", parts);
    }

    private static string LiveSettingJson(LiveSettingModel setting)
    {
        var fields = new List<string>
        {
            $"\"name\":{Str(setting.Name)}",
            $"\"type\":{Str(setting.Type)}",
            $"\"defaultValue\":{LiveSettingJsonLiteral(setting.Type, setting.DefaultValue)}"
        };
        if (setting.Min is not null)
        {
            fields.Add($"\"min\":{LiveSettingJsonLiteral(setting.Type, setting.Min)}");
        }

        if (setting.Max is not null)
        {
            fields.Add($"\"max\":{LiveSettingJsonLiteral(setting.Type, setting.Max)}");
        }

        return "{" + string.Join(",", fields) + "}";
    }

    private static string LiveSettingJsonType(string type)
        => type switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool => Str("Bool"),
            DotBoxDGenerationNames.ManifestTypes.Int => Str("I32"),
            DotBoxDGenerationNames.ManifestTypes.Long => Str("I64"),
            DotBoxDGenerationNames.ManifestTypes.Double => Str("F64"),
            DotBoxDGenerationNames.ManifestTypes.String => Str("String"),
            _ => throw new NotSupportedException("Live settings must use supported scalar types.")
        };

    private static string LiveSettingJsonLiteral(string type, string literal)
        => type switch
        {
            DotBoxDGenerationNames.ManifestTypes.Long => TrimLiteralSuffix(
                literal,
                DotBoxDGenerationNames.CSharpLiterals.Int64Suffix),
            DotBoxDGenerationNames.ManifestTypes.Double => TrimLiteralSuffix(
                literal,
                DotBoxDGenerationNames.CSharpLiterals.DoubleSuffix),
            DotBoxDGenerationNames.ManifestTypes.String => StringJsonLiteral(literal),
            _ => literal
        };

    private static string TrimLiteralSuffix(string literal, string suffix)
        => literal.EndsWith(suffix, StringComparison.Ordinal)
            ? literal.Substring(0, literal.Length - suffix.Length)
            : literal;

    private static string StringJsonLiteral(string literal)
    {
        if (literal == DotBoxDGenerationNames.CSharpLiterals.Null)
        {
            return literal;
        }

        var parsed = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(literal);
        return parsed is LiteralExpressionSyntax { Token.Value: string value }
            ? Str(value)
            : throw new NotSupportedException("Live setting string defaults must be string literals.");
    }

    private static bool ContainsUnsupported(EquatableArray<LiveSettingModel> liveSettings)
    {
        for (var i = 0; i < liveSettings.Count; i++)
        {
            if (liveSettings[i].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }
}
