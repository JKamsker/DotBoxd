using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class DispatcherCaseGenerator
{
    public static void Generate(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string receiver,
        CancellationToken ct)
    {
        if (method.UnsupportedReason is not null)
        {
            return;
        }

        sb.AppendLine($"                case \"{method.RpcName}\":");
        sb.AppendLine("                {");

        var requestParameters = DispatcherGeneratorHelpers.GetRequestParameters(method.Parameters, ct);
        if (requestParameters.Count == 0)
        {
            AppendNoPayloadGuard(sb);
        }
        else if (requestParameters.Count == 1)
        {
            var wireType = ProxyGenerationHelpers.GetWireType(requestParameters[0]);
            sb.AppendLine($"                    var arg = serializer.{ServicesGeneratorMemberNames.Serializer.Deserialize}<{wireType}>(payload);");
        }
        else if (requestParameters.Count > 1)
        {
            AppendTupleArgumentReader(sb, requestParameters, ct);
        }

        var locals = new GeneratedLocalNames(method.Parameters, ct);
        var argumentExpressions = BuildArgumentExpressions(
            sb,
            method,
            requestParameters.Count,
            locals,
            ct);
        var call = BuildCall(method, receiver, argumentExpressions, ct);

        GenerateReturn(sb, method, call);
        sb.AppendLine("                }");
    }

    private static void AppendNoPayloadGuard(StringBuilder sb)
    {
        sb.AppendLine("                    if (payload.Length != 0)");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        throw new {ServicesGeneratorTypeNames.GlobalServiceProtocolException}(\"Request payload is not allowed for a parameterless RPC method.\");");
        sb.AppendLine("                    }");
    }

    private static void AppendTupleArgumentReader(
        StringBuilder sb,
        List<ParameterModel> requestParameters,
        CancellationToken ct)
    {
        var tupleTypes = new StringBuilder();
        for (var i = 0; i < requestParameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
                tupleTypes.Append(", ");
            tupleTypes.Append(ProxyGenerationHelpers.GetWireType(requestParameters[i]));
        }

        sb.AppendLine($"                    var args = serializer.{ServicesGeneratorMemberNames.Serializer.Deserialize}<({tupleTypes})>(payload);");
    }

    private static string[] BuildArgumentExpressions(
        StringBuilder sb,
        MethodModel method,
        int requestParameterCount,
        GeneratedLocalNames locals,
        CancellationToken ct)
    {
        var argumentExpressions = new string[method.Parameters.Count];
        var argumentRequestIndex = 0;
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var parameter = method.Parameters[i];
            if (parameter.IsCancellationToken)
            {
                argumentExpressions[i] = "ct";
                continue;
            }

            argumentRequestIndex++;
            var source = requestParameterCount == 1
                ? "arg"
                : "args.Item" + argumentRequestIndex;
            if (parameter.StreamKind == ParameterStreamKind.None)
            {
                argumentExpressions[i] = source;
                continue;
            }

            var local = locals.Reserve("__dotboxd_arg" + argumentRequestIndex, ct);
            sb.AppendLine($"                    var {local} = {DispatcherGeneratorHelpers.BuildStreamingArgument(parameter, source)};");
            argumentExpressions[i] = local;
        }

        return argumentExpressions;
    }

    private static string BuildCall(
        MethodModel method,
        string receiver,
        string[] argumentExpressions,
        CancellationToken ct)
    {
        var argList = new StringBuilder();
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
                argList.Append(", ");
            argList.Append(argumentExpressions[i]);
        }

        var target = method.RequiresDispatcherReceiverCast
            ? $"(({method.ExplicitImplementationType}){receiver})"
            : receiver;
        return $"{target}.{method.Name}({argList})";
    }

    private static void GenerateReturn(StringBuilder sb, MethodModel method, string call)
    {
        switch (method.ReturnKind)
        {
            case MethodReturnKind.Void:
                sb.AppendLine($"                    {call};");
                sb.AppendLine("                    return;");
                break;

            case MethodReturnKind.Sync:
                sb.AppendLine($"                    var result = {call};");
                sb.AppendLine($"                    serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}(output, result);");
                sb.AppendLine("                    return;");
                break;

            case MethodReturnKind.Task:
                sb.AppendLine($"                    var __dotboxd_task = {call};");
                sb.AppendLine("                    if (!__dotboxd_task.IsCompletedSuccessfully)");
                sb.AppendLine("                    {");
                sb.AppendLine("                        await __dotboxd_task;");
                sb.AppendLine("                    }");
                sb.AppendLine("                    return;");
                break;

            case MethodReturnKind.ValueTask:
                sb.AppendLine($"                    var __dotboxd_task = {call};");
                sb.AppendLine("                    if (!__dotboxd_task.IsCompletedSuccessfully)");
                sb.AppendLine("                    {");
                sb.AppendLine("                        await __dotboxd_task;");
                sb.AppendLine("                        return;");
                sb.AppendLine("                    }");
                sb.AppendLine("                    __dotboxd_task.GetAwaiter().GetResult();");
                sb.AppendLine("                    return;");
                break;

            case MethodReturnKind.ValueTaskOf:
            case MethodReturnKind.TaskOf:
                GenerateAwaitedResult(sb, call);
                sb.AppendLine($"                    serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}(output, __dotboxd_result);");
                sb.AppendLine("                    return;");
                break;

            case MethodReturnKind.TaskOfSubService:
            case MethodReturnKind.ValueTaskOfSubService:
            case MethodReturnKind.SyncSubService:
                GenerateSubServiceReturn(sb, method, call);
                break;

            case MethodReturnKind.AsyncEnumerable:
            case MethodReturnKind.Stream:
            case MethodReturnKind.Pipe:
                sb.AppendLine($"                    var result = {call};");
                sb.AppendLine($"                    streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.SetResponse}(result);");
                sb.AppendLine("                    return;");
                break;

            case MethodReturnKind.TaskOfAsyncEnumerable:
            case MethodReturnKind.ValueTaskOfAsyncEnumerable:
            case MethodReturnKind.TaskOfStream:
            case MethodReturnKind.ValueTaskOfStream:
            case MethodReturnKind.TaskOfPipe:
            case MethodReturnKind.ValueTaskOfPipe:
                GenerateAwaitedResult(sb, call);
                sb.AppendLine($"                    streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.SetResponse}(__dotboxd_result);");
                sb.AppendLine("                    return;");
                break;
        }
    }

    private static void GenerateAwaitedResult(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    var __dotboxd_result = __dotboxd_task.IsCompletedSuccessfully");
        sb.AppendLine("                        ? __dotboxd_task.Result");
        sb.AppendLine("                        : await __dotboxd_task;");
    }

    private static void GenerateSubServiceReturn(
        StringBuilder sb,
        MethodModel method,
        string call)
    {
        var info = method.SubService!;
        if (method.ReturnKind == MethodReturnKind.SyncSubService)
        {
            sb.AppendLine($"                    var __sub = {call};");
        }
        else
        {
            GenerateAwaitedSubService(sb, call);
        }

        if (info.AllowsNull)
        {
            sb.AppendLine("                    if (__sub is null)");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}<{ServicesGeneratorTypeNames.NullableOf(ServicesGeneratorTypeNames.GlobalServiceHandle)}>(output, null);");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
        }

        sb.AppendLine("                    string __subId;");
        sb.AppendLine("                    try");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        __subId = registry.{ServicesGeneratorMemberNames.InstanceRegistry.Register}(\"{info.ServiceName}\", __sub);");
        sb.AppendLine("                    }");
        sb.AppendLine("                    catch");
        sb.AppendLine("                    {");
        GenerateSubServiceCleanup(sb);
        sb.AppendLine("                        throw;");
        sb.AppendLine("                    }");
        GenerateSubServiceHandleSerialization(sb, info.ServiceName);
    }

    private static void GenerateAwaitedSubService(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    var __sub = __dotboxd_task.IsCompletedSuccessfully");
        sb.AppendLine("                        ? __dotboxd_task.Result");
        sb.AppendLine("                        : await __dotboxd_task;");
    }

    private static void GenerateSubServiceCleanup(StringBuilder sb)
    {
        sb.AppendLine("                        try");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            if (__sub is {ServicesGeneratorTypeNames.GlobalIAsyncDisposable} __ad)");
        sb.AppendLine("                            {");
        sb.AppendLine("                                await __ad.DisposeAsync().ConfigureAwait(false);");
        sb.AppendLine("                            }");
        sb.AppendLine($"                            else if (__sub is {ServicesGeneratorTypeNames.GlobalIDisposable} __d)");
        sb.AppendLine("                            {");
        sb.AppendLine("                                __d.Dispose();");
        sb.AppendLine("                            }");
        sb.AppendLine("                        }");
        sb.AppendLine("                        catch");
        sb.AppendLine("                        {");
        sb.AppendLine("                            // Best-effort cleanup of the orphaned sub-service: a faulting disposer must");
        sb.AppendLine("                            // not replace the original registration failure that is about to be rethrown.");
        sb.AppendLine("                        }");
    }

    private static void GenerateSubServiceHandleSerialization(StringBuilder sb, string serviceName)
    {
        sb.AppendLine("                    try");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}(output, new {ServicesGeneratorTypeNames.GlobalServiceHandle} {{ {ServicesGeneratorMemberNames.ServiceHandle.ServiceName} = \"{serviceName}\", {ServicesGeneratorMemberNames.ServiceHandle.InstanceId} = __subId }});");
        sb.AppendLine("                    }");
        sb.AppendLine("                    catch");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        await registry.{ServicesGeneratorMemberNames.InstanceRegistry.ReleaseAsync}(\"{serviceName}\", __subId).ConfigureAwait(false);");
        sb.AppendLine("                        throw;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    return;");
    }
}
