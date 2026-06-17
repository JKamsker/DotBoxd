using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class DotBoxDCapabilityRequestSourceEmitter
{
    public static void Emit(StringBuilder builder, EquatableArray<string> capabilities)
    {
        builder.Append("            [");
        for (var i = 0; i < capabilities.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append("new ")
                .Append(TypeNames.GlobalCapabilityRequest)
                .Append('(');
            EmitCapabilityId(builder, capabilities[i]);
            builder.Append(", ")
                .Append(ReasonLiteral(capabilities[i]))
                .Append(')');
        }

        builder.AppendLine("],");
    }

    private static void EmitCapabilityId(StringBuilder builder, string capability)
    {
        if (string.Equals(
            capability,
            DotBoxDGenerationNames.Capabilities.MessageWrite,
            StringComparison.Ordinal))
        {
            builder.Append(TypeNames.GlobalPluginMessageBindings).Append(".CapabilityId");
            return;
        }

        builder.Append(LiteralReader.StringLiteral(capability));
    }

    private static string ReasonLiteral(string capability)
        => string.Equals(
            capability,
            DotBoxDGenerationNames.Capabilities.MessageWrite,
            StringComparison.Ordinal)
            ? LiteralReader.StringLiteral(DotBoxDGenerationNames.Capabilities.MessageWriteReason)
            : DotBoxDGenerationNames.CSharpLiterals.Null;
}
