using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ProxyInvocationEmitter
{
    public static void Emit(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
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
            case MethodReturnKind.Stream:
            case MethodReturnKind.Pipe:
                sb.AppendLine($"            return {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.AsyncEnumerable:
                sb.AppendLine($"            return {invocation};");
                break;
            case MethodReturnKind.Task:
            case MethodReturnKind.ValueTask:
                sb.AppendLine($"            await {invocation};");
                break;
            case MethodReturnKind.TaskOf:
            case MethodReturnKind.ValueTaskOf:
            case MethodReturnKind.TaskOfStream:
            case MethodReturnKind.ValueTaskOfStream:
            case MethodReturnKind.TaskOfPipe:
            case MethodReturnKind.ValueTaskOfPipe:
                sb.AppendLine($"            return await {invocation};");
                break;
            case MethodReturnKind.TaskOfAsyncEnumerable:
            case MethodReturnKind.ValueTaskOfAsyncEnumerable:
                sb.AppendLine($"            return await {invocation};");
                break;
            case MethodReturnKind.TaskOfSubService:
            case MethodReturnKind.ValueTaskOfSubService:
                EmitSubServiceReturn(sb, method, invocation, locals, ct);
                break;
        }
    }

    private static void EmitSubServiceReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct)
    {
        var info = method.SubService!;
        var subProxyType = ProxyGenerationHelpers.BuildSubProxyTypeName(info.QualifiedInterfaceName);
        var handleName = locals.Reserve("__sharpc_handle", ct);
        sb.AppendLine($"            var {handleName} = await {invocation};");
        if (info.AllowsNull)
        {
            // ServiceHandle is a struct, so the nullable wire type is Nullable<ServiceHandle>;
            // unwrap via .Value before reading InstanceId.
            sb.AppendLine($"            return {handleName} is null ? null : new {subProxyType}(this._invoker, {handleName}.Value.InstanceId);");
        }
        else
        {
            sb.AppendLine($"            return new {subProxyType}(this._invoker, {handleName}.InstanceId);");
        }
    }
}
