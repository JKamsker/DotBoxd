using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ProxyInvocationEmitter
{
    public static void Emit(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        CancellationToken ct)
    {
        switch (method.ReturnKind)
        {
            case MethodReturnKind.Void:
                sb.AppendLine($"            {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.Sync:
                sb.AppendLine($"            return {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.ValueTask:
                sb.AppendLine($"            await {invocation};");
                break;
            case MethodReturnKind.TaskOf:
            case MethodReturnKind.ValueTaskOf:
                sb.AppendLine($"            return await {invocation};");
                break;
            case MethodReturnKind.TaskOfSubService:
            case MethodReturnKind.ValueTaskOfSubService:
                EmitSubServiceReturn(sb, method, invocation, ct);
                break;
        }
    }

    private static void EmitSubServiceReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        CancellationToken ct)
    {
        var info = method.SubService!;
        var subProxyType = ProxyGenerationHelpers.BuildSubProxyTypeName(info.QualifiedInterfaceName);
        var handleName = ProxyGenerationHelpers.UniqueGeneratedLocalName(
            method.Parameters,
            "__sharpc_handle",
            ct);
        sb.AppendLine($"            var {handleName} = await {invocation};");
        if (info.AllowsNull)
        {
            sb.AppendLine($"            return {handleName} is null ? null : new {subProxyType}(this._client, {handleName}.InstanceId);");
        }
        else
        {
            sb.AppendLine($"            return new {subProxyType}(this._client, {handleName}.InstanceId);");
        }
    }
}
