using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class RpcKernelModelFactory
{
    private static string PluginId(IReadOnlyList<AttributeData> attributes, string kernelName)
    {
        if (attributes.Count > 0)
        {
            var args = attributes[0].ConstructorArguments;
            if (args.Length > 0 && args[0].Value is string id)
            {
                return id;
            }

            if (args.Length > 1 && args[1].Value is string newShapeId)
            {
                return newShapeId;
            }
        }

        return KernelId(kernelName);
    }

    private static INamedTypeSymbol? ServiceType(IReadOnlyList<AttributeData> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var args = attributes[0].ConstructorArguments;
        return args.Length > 1 && args[1].Value is INamedTypeSymbol serviceType
            ? serviceType
            : null;
    }

    private static INamedTypeSymbol? GraftType(IReadOnlyList<AttributeData> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var args = attributes[0].ConstructorArguments;
        return args.Length > 0 && args[0].Value is INamedTypeSymbol graftType
            ? graftType
            : null;
    }

    private static string KernelId(string kernelName)
    {
        var name = kernelName.EndsWith(DotBoxDGenerationNames.KernelSuffix, StringComparison.Ordinal)
            ? kernelName.Substring(0, kernelName.Length - DotBoxDGenerationNames.KernelSuffix.Length)
            : kernelName;
        return ToKebabCase(name);
    }

    private static string ToKebabCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
