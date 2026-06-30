using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static partial class ProxyGenerator
{
    /// <summary>
    /// Builds the call to <c>_invoker.InvokeAsync</c> or <c>_invoker.InvokeOnInstanceAsync</c>.
    /// For sub-service-returning methods, the wire response type is always
    /// <c>ServiceHandle</c>; the caller wraps it in a generated sub-proxy. The emitted
    /// expression branches on <c>_instanceId</c> so the same proxy class can serve both
    /// the top-level and the nested-instance call paths.
    /// </summary>
    private static (string Invocation, System.Collections.Generic.List<(string HandleName, string ReservedName)>? Reservations) BuildClientInvocation(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string ctArg,
        GeneratedLocalNames locals,
        CancellationToken ct,
        string indent = "            ")
    {
        var isSubServiceReturn = NamingHelpers.IsSubServiceReturn(method.ReturnKind);
        var hasReturn = NamingHelpers.HasReturnValue(method.ReturnKind);
        var returnType = isSubServiceReturn
            ? GetServiceHandleType(method)
            : method.UnwrappedReturnType is null
                ? null
                : ProxyGenerationHelpers.GetWireType(method.UnwrappedReturnType);
        var requestParameters = ProxyGenerationHelpers.GetRequestParameters(method.Parameters, ct);
        var streamSetup = ProxyStreamSetupEmitter.Emit(sb, method, requestParameters, locals, ct, indent);
        var streamArgument = streamSetup.ArgumentName;
        var useStreamAwareTaskValueInvocation =
            streamArgument is not null && (method.ReturnKind is MethodReturnKind.ValueTask or MethodReturnKind.ValueTaskOf);
        var svc = service.ServiceName;
        var rpc = method.RpcName;
        var singletonMethod = GetInvokerMethod(
            method.ReturnKind,
            isInstanceScoped: false,
            useStreamAwareTaskValueInvocation);
        var instanceMethod = GetInvokerMethod(
            method.ReturnKind,
            isInstanceScoped: true,
            useStreamAwareTaskValueInvocation);

        // Build the type-parameter list and argument list once; switch between the two
        // overload prefixes via the ternary.
        string typeArgs;
        string callArgs;        // arguments after (service, method) for the singleton overload
        string callArgsInst;    // arguments after (service, instanceId, method) for instance

        if (requestParameters.Count == 0)
        {
            typeArgs = BuildTypeArgs(method.ReturnKind, requestType: null, returnType, hasReturn);
            callArgs = $"\"{svc}\", \"{rpc}\", {ctArg}";
            callArgsInst = $"\"{svc}\", this._instanceId!, \"{rpc}\", {ctArg}";
        }
        else if (requestParameters.Count == 1)
        {
            var p = requestParameters[0];
            var wireType = ProxyGenerationHelpers.GetWireType(p);
            var wireArgument = GetWireArgument(p, requestIndex: 0, streamSetup.Handles);
            typeArgs = BuildTypeArgs(method.ReturnKind, wireType, returnType, hasReturn);
            var streamArg = NeedsStreamArgument(method.ReturnKind, streamArgument)
                ? $", {streamArgument ?? NullStreamArray()}"
                : string.Empty;
            callArgs = $"\"{svc}\", \"{rpc}\", {wireArgument}{streamArg}, {ctArg}";
            callArgsInst = $"\"{svc}\", this._instanceId!, \"{rpc}\", {wireArgument}{streamArg}, {ctArg}";
        }
        else
        {
            var tupleTypes = new StringBuilder();
            var tupleValues = new StringBuilder();
            for (var i = 0; i < requestParameters.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (i > 0)
                {
                    tupleTypes.Append(", ");
                    tupleValues.Append(", ");
                }
                tupleTypes.Append(ProxyGenerationHelpers.GetWireType(requestParameters[i]));
                tupleValues.Append(GetWireArgument(requestParameters[i], i, streamSetup.Handles));
            }

            typeArgs = BuildTypeArgs(method.ReturnKind, $"({tupleTypes})", returnType, hasReturn);
            var streamArg = NeedsStreamArgument(method.ReturnKind, streamArgument)
                ? $", {streamArgument ?? NullStreamArray()}"
                : string.Empty;
            callArgs = $"\"{svc}\", \"{rpc}\", ({tupleValues}){streamArg}, {ctArg}";
            callArgsInst = $"\"{svc}\", this._instanceId!, \"{rpc}\", ({tupleValues}){streamArg}, {ctArg}";
        }

        var invocation =
            $"(this._instanceId is null ? this._invoker.{singletonMethod}{typeArgs}({callArgs}) : this._invoker.{instanceMethod}{typeArgs}({callArgsInst}))";
        if (useStreamAwareTaskValueInvocation)
        {
            invocation = method.ReturnKind == MethodReturnKind.ValueTask ? $"new {ServicesGeneratorTypeNames.GlobalValueTask}({invocation})" :
                $"new {ServicesGeneratorTypeNames.Generic(ServicesGeneratorTypeNames.GlobalValueTask, returnType!)}({invocation})";
        }

        return (invocation, streamSetup.Reservations);
    }

    private static string GetServiceHandleType(MethodModel method) =>
        method.SubService?.AllowsNull == true
            ? ServicesGeneratorTypeNames.NullableOf(ServicesGeneratorTypeNames.GlobalServiceHandle)
            : ServicesGeneratorTypeNames.GlobalServiceHandle;

    private static string BuildTypeArgs(
        MethodReturnKind returnKind,
        string? requestType,
        string? returnType,
        bool hasReturn)
    {
        if (NamingHelpers.IsAsyncEnumerableReturn(returnKind))
        {
            return requestType is null
                ? $"<{returnType}>"
                : $"<{requestType}, {returnType}>";
        }

        if (NamingHelpers.IsStreamReturn(returnKind) || NamingHelpers.IsPipeReturn(returnKind))
        {
            return requestType is null ? string.Empty : $"<{requestType}>";
        }

        if (requestType is null)
        {
            return hasReturn ? $"<{returnType}>" : string.Empty;
        }

        return hasReturn ? $"<{requestType}, {returnType}>" : $"<{requestType}>";
    }

    private static string NullStreamArray() =>
        $"({ServicesGeneratorTypeNames.ArrayOf(ServicesGeneratorTypeNames.GlobalRpcStreamAttachment)}?)null";

    private static bool NeedsStreamArgument(MethodReturnKind returnKind, string? streamArgument) =>
        streamArgument is not null ||
        NamingHelpers.IsStreamReturn(returnKind) ||
        NamingHelpers.IsPipeReturn(returnKind) ||
        NamingHelpers.IsAsyncEnumerableReturn(returnKind);

    private static string GetWireArgument(
        ParameterModel parameter,
        int requestIndex,
        System.Collections.Generic.Dictionary<int, string> streamHandles) =>
        parameter.StreamKind == ParameterStreamKind.None
            ? ProxyGenerationHelpers.GetWireArgument(parameter)
            : streamHandles[requestIndex];

    private static string GetInvokerMethod(
        MethodReturnKind returnKind,
        bool isInstanceScoped,
        bool useStreamAwareTaskValueInvocation = false)
    {
        if (NamingHelpers.IsStreamReturn(returnKind))
        {
            return isInstanceScoped ? "InvokeStreamOnInstanceAsync" : "InvokeStreamAsync";
        }

        if (NamingHelpers.IsPipeReturn(returnKind))
        {
            return isInstanceScoped ? "InvokePipeOnInstanceAsync" : "InvokePipeAsync";
        }

        if (NamingHelpers.IsAsyncEnumerableReturn(returnKind))
        {
            var eager = returnKind == MethodReturnKind.TaskOfAsyncEnumerable ||
                returnKind == MethodReturnKind.ValueTaskOfAsyncEnumerable;
            return isInstanceScoped
                ? eager ? "InvokeAsyncEnumerableOnInstanceAsync" : "InvokeAsyncEnumerableOnInstance"
                : eager ? "InvokeAsyncEnumerableAsync" : "InvokeAsyncEnumerable";
        }

        if (returnKind is MethodReturnKind.ValueTask or MethodReturnKind.ValueTaskOf &&
            !useStreamAwareTaskValueInvocation)
        {
            return isInstanceScoped ? "InvokeValueOnInstanceAsync" : "InvokeValueAsync";
        }

        return isInstanceScoped ? "InvokeOnInstanceAsync" : "InvokeAsync";
    }
}
