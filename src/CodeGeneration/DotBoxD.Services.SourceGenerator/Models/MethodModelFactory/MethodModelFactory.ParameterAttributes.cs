using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static string BuildCallerInfoAttributePrefix(
        IParameterSymbol parameter,
        CancellationToken ct)
    {
        var attributes = new StringBuilder();
        var hasDateTimeConstant = HasDateTimeConstantAttribute(parameter);
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

                case "System.Runtime.InteropServices.OptionalAttribute" when hasDateTimeConstant:
                    attributes.Append("[global::System.Runtime.InteropServices.OptionalAttribute] ");
                    break;

                case "System.Runtime.CompilerServices.DateTimeConstantAttribute":
                    AppendDateTimeConstantAttribute(attributes, parameter);
                    break;
            }
        }

        return attributes.ToString();
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
