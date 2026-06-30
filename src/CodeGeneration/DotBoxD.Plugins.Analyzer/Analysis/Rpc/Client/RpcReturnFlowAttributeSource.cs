using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcReturnFlowAttributeSource
{
    public static void Append(StringBuilder builder, IMethodSymbol method, string indent)
    {
        foreach (var attribute in method.GetReturnTypeAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    AppendSimple(
                        builder,
                        indent,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    AppendSimple(
                        builder,
                        indent,
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    AppendStringArgument(
                        builder,
                        indent,
                        attribute,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                    break;
            }
        }
    }

    private static void AppendSimple(
        StringBuilder builder,
        string indent,
        string attributeType)
    {
        builder.Append(indent)
            .Append("[return: ")
            .Append(attributeType)
            .AppendLine("]");
    }

    private static void AppendStringArgument(
        StringBuilder builder,
        string indent,
        AttributeData attribute,
        string attributeType)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        builder.Append(indent)
            .Append("[return: ")
            .Append(attributeType)
            .Append('(')
            .Append(LiteralReader.StringLiteral(value))
            .AppendLine(")]");
    }
}
