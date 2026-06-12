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
        CancellationToken ct,
        string indent = "            ",
        bool captureSynchronousExceptions = true)
    {
        switch (method.ReturnKind)
        {
            case MethodReturnKind.Void:
                sb.AppendLine($"{indent}{invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.Sync:
                sb.AppendLine($"{indent}return {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.Stream:
            case MethodReturnKind.Pipe:
                sb.AppendLine($"{indent}return {invocation}.GetAwaiter().GetResult();");
                break;
            case MethodReturnKind.AsyncEnumerable:
                sb.AppendLine($"{indent}return {invocation};");
                break;
            case MethodReturnKind.Task:
                EmitTaskLikeReturn(sb, method, invocation, locals, ct, indent, captureSynchronousExceptions);
                break;
            case MethodReturnKind.ValueTask:
                EmitTaskLikeReturn(
                    sb,
                    method,
                    $"new global::System.Threading.Tasks.ValueTask({invocation})",
                    locals,
                    ct,
                    indent,
                    captureSynchronousExceptions);
                break;
            case MethodReturnKind.TaskOf:
            case MethodReturnKind.TaskOfStream:
            case MethodReturnKind.TaskOfPipe:
            case MethodReturnKind.TaskOfAsyncEnumerable:
                EmitTaskLikeReturn(sb, method, invocation, locals, ct, indent, captureSynchronousExceptions);
                break;
            case MethodReturnKind.ValueTaskOfStream:
            case MethodReturnKind.ValueTaskOfPipe:
            case MethodReturnKind.ValueTaskOfAsyncEnumerable:
                EmitTaskLikeReturn(
                    sb,
                    method,
                    $"new global::System.Threading.Tasks.ValueTask<{ProxyFaultedReturnEmitter.GetValueTaskResultType(method)}>({invocation})",
                    locals,
                    ct,
                    indent,
                    captureSynchronousExceptions);
                break;
            case MethodReturnKind.ValueTaskOf:
                EmitTaskLikeReturn(sb, method, invocation, locals, ct, indent, captureSynchronousExceptions);
                break;
            case MethodReturnKind.TaskOfSubService:
            case MethodReturnKind.ValueTaskOfSubService:
                EmitSubServiceReturn(sb, method, invocation, locals, ct, indent);
                break;
        }
    }

    private static void EmitTaskLikeReturn(
        StringBuilder sb,
        MethodModel method,
        string returnExpression,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent,
        bool captureSynchronousExceptions)
    {
        if (!captureSynchronousExceptions)
        {
            sb.AppendLine($"{indent}return {returnExpression};");
            return;
        }

        var exceptionName = locals.Reserve("__sharpc_ex", ct);
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return {returnExpression};");
        sb.AppendLine($"{indent}}}");
        var canceledName = locals.Reserve("__sharpc_canceled", ct);
        sb.AppendLine($"{indent}catch (global::System.OperationCanceledException {canceledName}) when ({canceledName}.CancellationToken.IsCancellationRequested)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return {ProxyFaultedReturnEmitter.BuildCanceled(method, canceledName)};");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}catch (global::System.Exception {exceptionName})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return {ProxyFaultedReturnEmitter.Build(method, exceptionName)};");
        sb.AppendLine($"{indent}}}");
    }

    private static void EmitSubServiceReturn(
        StringBuilder sb,
        MethodModel method,
        string invocation,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent)
    {
        var info = method.SubService!;
        var subProxyType = ProxyGenerationHelpers.BuildSubProxyTypeName(info.QualifiedInterfaceName);
        var handleName = locals.Reserve("__sharpc_handle", ct);
        sb.AppendLine($"{indent}var {handleName} = await {invocation};");
        if (info.AllowsNull)
        {
            // ServiceHandle is a struct, so the nullable wire type is Nullable<ServiceHandle>;
            // unwrap via .Value before reading InstanceId.
            sb.AppendLine($"{indent}return {handleName} is null ? null : new {subProxyType}(this._invoker, {handleName}.Value.InstanceId);");
        }
        else
        {
            sb.AppendLine($"{indent}return new {subProxyType}(this._invoker, {handleName}.InstanceId);");
        }
    }
}
