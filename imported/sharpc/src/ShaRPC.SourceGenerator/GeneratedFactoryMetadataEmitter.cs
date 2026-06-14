using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class GeneratedFactoryMetadataEmitter
{
    public static string MethodArrayName(int serviceIndex) =>
        "s_service" + serviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "Methods";

    public static void AppendMethodArrays(
        StringBuilder sb,
        EquatableArray<ServiceModel> services,
        CancellationToken ct)
    {
        for (var i = 0; i < services.Array.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
            {
                sb.AppendLine();
            }

            AppendMethodArray(sb, MethodArrayName(i), services.Array[i], ct);
        }
    }

    private static void AppendMethodArray(
        StringBuilder sb,
        string arrayName,
        ServiceModel service,
        CancellationToken ct)
    {
        sb.AppendLine($"        private static readonly global::ShaRPC.Core.Generated.ShaRpcGeneratedMethod[] {arrayName} =");
        sb.AppendLine("        {");

        foreach (var method in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (method.UnsupportedReason is not null)
            {
                continue;
            }

            AppendMethod(sb, method, ct);
        }

        sb.AppendLine("        };");
    }

    private static void AppendMethod(
        StringBuilder sb,
        MethodModel method,
        CancellationToken ct)
    {
        sb.AppendLine("            new global::ShaRPC.Core.Generated.ShaRpcGeneratedMethod(");
        sb.AppendLine($"                \"{LiteralHelpers.EscapeStringLiteral(IdentifierHelpers.UnescapeIdentifier(method.Name))}\",");
        sb.AppendLine($"                \"{LiteralHelpers.EscapeStringLiteral(method.RawRpcName)}\",");
        sb.AppendLine($"                typeof({method.MetadataReturnType}),");
        sb.AppendLine($"                {TypeExpression(method.MetadataResultType)},");
        sb.AppendLine($"                {ReturnKindExpression(method.ReturnKind)},");
        sb.AppendLine($"                {BoolLiteral(NamingHelpers.IsSubServiceReturn(method.ReturnKind))},");
        sb.AppendLine("                new global::ShaRPC.Core.Generated.ShaRpcGeneratedParameter[]");
        sb.AppendLine("                {");
        AppendParameters(sb, method.Parameters, ct);
        sb.AppendLine("                }),");
    }

    private static void AppendParameters(
        StringBuilder sb,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct)
    {
        for (var i = 0; i < parameters.Array.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var parameter = parameters.Array[i];
            sb.AppendLine("                    new global::ShaRPC.Core.Generated.ShaRpcGeneratedParameter(");
            sb.AppendLine($"                        \"{LiteralHelpers.EscapeStringLiteral(IdentifierHelpers.UnescapeIdentifier(parameter.Name))}\",");
            sb.AppendLine($"                        typeof({parameter.MetadataType}),");
            sb.AppendLine($"                        {i.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"                        {BoolLiteral(parameter.IsCancellationToken)},");
            sb.AppendLine($"                        {BoolLiteral(parameter.HasDefaultValue)},");
            sb.AppendLine($"                        {DefaultValueExpression(parameter)}),");
        }
    }

    private static string TypeExpression(string? type) =>
        string.IsNullOrEmpty(type)
            ? "null"
            : "typeof(" + type + ")";

    private static string DefaultValueExpression(ParameterModel parameter)
    {
        if (!parameter.HasDefaultValue ||
            parameter.IsCancellationToken ||
            parameter.DefaultValueLiteral.Length == 0)
        {
            return "null";
        }

        return parameter.DefaultValueLiteral;
    }

    private static string ReturnKindExpression(MethodReturnKind returnKind) =>
        "global::ShaRPC.Core.Generated.ShaRpcGeneratedReturnKind." + ReturnKindName(returnKind);

    private static string ReturnKindName(MethodReturnKind returnKind) => returnKind switch
    {
        MethodReturnKind.Void => "Void",
        MethodReturnKind.Sync => "Sync",
        MethodReturnKind.Task => "Task",
        MethodReturnKind.TaskOf => "TaskOfT",
        MethodReturnKind.ValueTask => "ValueTask",
        MethodReturnKind.ValueTaskOf => "ValueTaskOfT",
        MethodReturnKind.TaskOfSubService => "TaskOfNestedService",
        MethodReturnKind.ValueTaskOfSubService => "ValueTaskOfNestedService",
        MethodReturnKind.AsyncEnumerable => "AsyncEnumerable",
        MethodReturnKind.TaskOfAsyncEnumerable => "TaskOfAsyncEnumerable",
        MethodReturnKind.ValueTaskOfAsyncEnumerable => "ValueTaskOfAsyncEnumerable",
        MethodReturnKind.Stream => "Stream",
        MethodReturnKind.TaskOfStream => "TaskOfStream",
        MethodReturnKind.ValueTaskOfStream => "ValueTaskOfStream",
        MethodReturnKind.Pipe => "Pipe",
        MethodReturnKind.TaskOfPipe => "TaskOfPipe",
        MethodReturnKind.ValueTaskOfPipe => "ValueTaskOfPipe",
        _ => "Void",
    };

    private static string BoolLiteral(bool value) => value ? "true" : "false";
}
