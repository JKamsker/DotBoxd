namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// Emits the reflection-free, box-free reader a lowered remote <c>RunLocal</c> chain uses to turn the pushed
/// <c>KernelRpcValue</c> back into its projected CLR type — the decode-side counterpart to the runtime's
/// <c>LocalCallbackProjection</c> encode. It reuses <see cref="RpcKernelValueConversionEmitter"/>,
/// the same proven reader the server-extension client proxy emits, so RunLocal decode rides the same
/// generated path: scalars/enums from typed <c>KernelRpcValue</c> fields, lists/arrays via <c>GetItem(i)</c>,
/// and DTOs through the real constructor with field-count guards — no <c>SandboxValue</c> graph, no reflection.
/// </summary>
internal static class RpcLocalDecoderEmitter
{
    /// <summary>
    /// Returns the <c>ReadProjected</c> method plus its conversion helpers for appending to the per-chain
    /// package class, or <c>null</c> when <paramref name="projectedType"/> is not wire-eligible — in which case
    /// the chain keeps the reflective 2-arg registration so non-eligible types do not regress.
    /// </summary>
    public static string? TryEmit(ITypeSymbol projectedType)
    {
        try
        {
            if (projectedType is INamedTypeSymbol { IsAnonymousType: true } anonymousType)
            {
                return TryEmitAnonymous(anonymousType);
            }

            var conv = new RpcKernelValueConversionEmitter();
            var payload = new RpcKernelPayloadReadEmitter();
            // Compute the read expression first: it appends any nested list/DTO helpers to conv.Helpers, so the
            // ReadProjected body never splices a helper into the middle of itself.
            var readExpression = conv.ReadExpression(projectedType, "value");
            var payloadExpression = payload.ReadExpression(projectedType, "reader");
            var typeName = projectedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var builder = new StringBuilder();
            builder.Append("    public static ").Append(typeName)
                .AppendLine(" ReadProjected(global::DotBoxD.Plugins.KernelRpcValue value)");
            builder.Append("        => ").Append(readExpression).AppendLine(";");
            builder.AppendLine();
            builder.Append(conv.Helpers);
            AppendPayloadReader(builder, typeName, payloadExpression);
            builder.Append(payload.Helpers);
            return builder.ToString();
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string TryEmitAnonymous(INamedTypeSymbol projectedType)
    {
        var fields = DotBoxDRpcTypeMapper.RecordFields(projectedType);
        if (fields.Count == 0)
        {
            throw new NotSupportedException("Anonymous projection must expose at least one field.");
        }

        var conv = new RpcKernelValueConversionEmitter();
        var payload = new RpcKernelPayloadReadEmitter();
        var arguments = new string[fields.Count];
        var payloadArguments = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            arguments[i] = conv.ReadExpression(fields[i].Type, "value.GetItem(" + i + ")");
            payloadArguments[i] = payload.ReadExpression(fields[i].Type, "reader");
        }

        var builder = new StringBuilder();
        builder.AppendLine("    public static TProjected ReadProjected<TProjected>(global::DotBoxD.Plugins.KernelRpcValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        builder.Append("        if (value.ItemCount != ").Append(fields.Count).AppendLine(")");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated anonymous projection shape.\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.Append("        return (TProjected)(object)new { ");
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Identifier(fields[i].Name)).Append(" = ").Append(arguments[i]);
        }

        builder.AppendLine(" };");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append(conv.Helpers);
        AppendAnonymousPayloadReader(builder, fields, payloadArguments, payload);
        builder.Append(payload.Helpers);
        return builder.ToString();
    }

    private static void AppendPayloadReader(StringBuilder builder, string typeName, string payloadExpression)
    {
        builder.Append("    public static ").Append(typeName)
            .AppendLine(" ReadProjectedPayload(global::System.ReadOnlyMemory<byte> payload)");
        builder.AppendLine("    {");
        builder.AppendLine("        var reader = new global::DotBoxD.Plugins.KernelRpcPayloadReader(payload.Span);");
        builder.Append("        var result = ").Append(payloadExpression).AppendLine(";");
        builder.AppendLine("        reader.EnsureConsumed();");
        builder.AppendLine("        return result;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendAnonymousPayloadReader(
        StringBuilder builder,
        IReadOnlyList<RecordMember> fields,
        IReadOnlyList<string> payloadArguments,
        RpcKernelPayloadReadEmitter payload)
    {
        builder.AppendLine("    public static TProjected ReadProjectedPayload<TProjected>(global::System.ReadOnlyMemory<byte> payload)");
        builder.AppendLine("    {");
        builder.AppendLine("        var reader = new global::DotBoxD.Plugins.KernelRpcPayloadReader(payload.Span);");
        payload.AppendRecordHeaderGuard(builder, fields.Count);
        builder.AppendLine();
        builder.Append("        var result = (TProjected)(object)new { ");
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Identifier(fields[i].Name)).Append(" = ").Append(payloadArguments[i]);
        }

        builder.AppendLine(" };");
        builder.AppendLine("        reader.EnsureConsumed();");
        builder.AppendLine("        return result;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static string Identifier(string name) => "@" + name;
}
