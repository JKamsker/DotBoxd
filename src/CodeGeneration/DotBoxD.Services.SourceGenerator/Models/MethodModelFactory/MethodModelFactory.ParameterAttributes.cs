using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static string BuildCallerInfoAttributePrefix(
        IParameterSymbol parameter,
        CancellationToken ct,
        bool preserveOptionalAttributeDefault)
    {
        var attributes = new StringBuilder();
        var hasDateTimeConstant = HasDateTimeConstantAttribute(parameter);
        var hasDecimalConstant = HasDecimalConstantAttribute(parameter);
        var preserveOptionalAttribute =
            preserveOptionalAttributeDefault ||
            hasDateTimeConstant ||
            hasDecimalConstant;
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
                    AppendDateTimeConstantAttribute(attributes, parameter);
                    break;

                case "System.Runtime.CompilerServices.DecimalConstantAttribute":
                    AppendDecimalConstantAttribute(attributes, parameter);
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

    private static void AppendDateTimeConstantAttribute(StringBuilder sb, IParameterSymbol parameter)
    {
        if (!TryGetDateTimeConstantTicks(parameter, out var ticks))
        {
            return;
        }

        sb.Append("[global::System.Runtime.CompilerServices.DateTimeConstantAttribute(")
            .Append(ticks)
            .Append("L)] ");
    }
}
