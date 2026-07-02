using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class DispatcherGeneratorHelpers
{
    public static string BuildStreamingArgument(ParameterModel parameter, string source) =>
        parameter.StreamKind switch
        {
            ParameterStreamKind.Stream => $"streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.GetStream}({source})",
            ParameterStreamKind.Pipe => $"streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.GetPipe}({source})",
            ParameterStreamKind.AsyncEnumerable => $"streaming.{ServicesGeneratorMemberNames.RpcStreamingContext.GetAsyncEnumerable}<{parameter.StreamItemType}>({source})",
            _ => source,
        };

    public static List<ParameterModel> GetRequestParameters(
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct)
    {
        var requestParameters = new List<ParameterModel>();
        foreach (var p in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();
            if (!p.IsCancellationToken)
            {
                requestParameters.Add(p);
            }
        }

        return requestParameters;
    }

    public static bool CanDispatchWithoutStreaming(ServiceModel service)
    {
        foreach (var method in service.Methods.Array)
        {
            if (method.UnsupportedReason is null && UsesStreaming(method))
            {
                return false;
            }
        }

        return true;
    }

    private static bool UsesStreaming(MethodModel method)
    {
        if (NamingHelpers.IsStreamReturn(method.ReturnKind) ||
            NamingHelpers.IsPipeReturn(method.ReturnKind) ||
            NamingHelpers.IsAsyncEnumerableReturn(method.ReturnKind))
        {
            return true;
        }

        foreach (var parameter in method.Parameters.Array)
        {
            if (parameter.StreamKind != ParameterStreamKind.None)
            {
                return true;
            }
        }

        return false;
    }
}
