using System.Text;
using System.Threading;
using DotBoxD.CodeGeneration.Shared.Defaults;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static string BuildCallerInfoAttributePrefix(
        IParameterSymbol parameter,
        CancellationToken ct,
        bool preserveOptionalAttributeDefault,
        bool preserveMetadataDefaultAttributes)
    {
        var attributes = new StringBuilder();
        var hasDateTimeConstant = preserveMetadataDefaultAttributes &&
            ParameterDefaultValueEmitter.HasDateTimeConstantAttribute(parameter);
        var hasDecimalConstant = preserveMetadataDefaultAttributes &&
            ParameterDefaultValueEmitter.HasDecimalConstantAttribute(parameter);
        var hasDefaultParameterValue = preserveMetadataDefaultAttributes &&
            ParameterDefaultValueEmitter.HasDefaultParameterValueAttribute(parameter);
        var preserveOptionalAttribute =
            preserveOptionalAttributeDefault ||
            hasDateTimeConstant ||
            hasDecimalConstant ||
            hasDefaultParameterValue;
        if (preserveOptionalAttribute)
        {
            attributes.Append("[global::System.Runtime.InteropServices.OptionalAttribute] ");
        }

        foreach (var attr in parameter.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            switch (attr.AttributeClass?.ToDisplayString())
            {
                case "System.Runtime.CompilerServices.CallerMemberNameAttribute":
                    attributes.Append("[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.CallerFilePathAttribute":
                    attributes.Append("[global::System.Runtime.CompilerServices.CallerFilePathAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.CallerLineNumberAttribute":
                    attributes.Append("[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute":
                    AppendCallerArgumentExpressionAttribute(attributes, attr);
                    break;

                case "System.Runtime.CompilerServices.DateTimeConstantAttribute":
                    if (preserveMetadataDefaultAttributes)
                    {
                        attributes.Append(ParameterDefaultValueEmitter.FormatDateTimeConstantAttribute(parameter));
                    }

                    break;

                case "System.Runtime.CompilerServices.DecimalConstantAttribute":
                    if (preserveMetadataDefaultAttributes)
                    {
                        attributes.Append(ParameterDefaultValueEmitter.FormatDecimalConstantAttribute(parameter));
                    }

                    break;

                case "System.Runtime.InteropServices.DefaultParameterValueAttribute":
                    if (preserveMetadataDefaultAttributes)
                    {
                        attributes.Append(ParameterDefaultValueEmitter.FormatDefaultParameterValueAttribute(parameter));
                    }

                    break;

                case "System.Diagnostics.CodeAnalysis.AllowNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.AllowNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.DisallowNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute":
                    AppendBooleanArgumentAttribute(
                        attributes,
                        attr,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute":
                    AppendBooleanArgumentAttribute(
                        attributes,
                        attr,
                        "global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    AppendStringArgumentAttribute(
                        attributes,
                        attr,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                    break;
            }
        }

        return attributes.ToString();
    }

    private static string BuildReturnFlowAttributePrefix(IMethodSymbol method, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in method.GetReturnTypeAttributes())
        {
            ct.ThrowIfCancellationRequested();

            switch (attr.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    AppendReturnSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    AppendReturnSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    AppendReturnStringArgumentAttribute(
                        attributes,
                        attr,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                    break;
            }
        }

        return attributes.ToString();
    }

    private static void AppendSimpleAttribute(StringBuilder sb, string attributeType)
    {
        sb.Append("[")
            .Append(attributeType)
            .Append("] ");
    }

    private static void AppendBooleanArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not bool value)
        {
            return;
        }

        sb.Append("[")
            .Append(attributeType)
            .Append("(")
            .Append(value ? "true" : "false")
            .Append(")] ");
    }

    private static void AppendStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        sb.Append("[")
            .Append(attributeType)
            .Append("(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(value))
            .Append("\")] ");
    }

    private static void AppendReturnSimpleAttribute(StringBuilder sb, string attributeType)
    {
        sb.Append("[return: ")
            .Append(attributeType)
            .AppendLine("]");
    }

    private static void AppendReturnStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        sb.Append("[return: ")
            .Append(attributeType)
            .Append("(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(value))
            .AppendLine("\")]");
    }

    private static void AppendCallerArgumentExpressionAttribute(StringBuilder sb, AttributeData attr)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string parameterName)
        {
            return;
        }

        sb.Append("[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(parameterName))
            .Append("\")] ");
    }

}
