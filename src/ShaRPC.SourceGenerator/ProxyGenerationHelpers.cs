using System.Collections.Generic;
using System.Text;

namespace ShaRPC.SourceGenerator;

internal static class ProxyGenerationHelpers
{
    public static void AppendParameterList(StringBuilder sb, EquatableArray<ParameterModel> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var p = parameters[i];
            sb.Append(p.RefKindKeyword).Append(p.Type).Append(' ').Append(p.Name);
            if (p.IsCancellationToken && p.HasDefaultValue)
            {
                sb.Append(" = default");
            }
        }
    }

    public static string GetCancellationTokenArgument(EquatableArray<ParameterModel> parameters)
    {
        foreach (var p in parameters.Array)
        {
            if (p.IsCancellationToken)
            {
                return p.Name;
            }
        }

        return "default";
    }

    public static List<ParameterModel> GetRequestParameters(EquatableArray<ParameterModel> parameters)
    {
        var requestParameters = new List<ParameterModel>();
        foreach (var p in parameters.Array)
        {
            if (!p.IsCancellationToken)
            {
                requestParameters.Add(p);
            }
        }

        return requestParameters;
    }

    public static string BuildSubProxyTypeName(string qualifiedInterfaceName)
    {
        const string globalPrefix = "global::";
        var startsWithGlobal = qualifiedInterfaceName.StartsWith(globalPrefix, System.StringComparison.Ordinal);
        var searchStart = startsWithGlobal ? globalPrefix.Length : 0;
        var lastDot = qualifiedInterfaceName.LastIndexOf('.');
        var hasNamespace = lastDot >= searchStart;
        var qualifierPart = hasNamespace
            ? qualifiedInterfaceName.Substring(0, lastDot + 1)
            : startsWithGlobal ? globalPrefix : string.Empty;
        var simpleName = hasNamespace
            ? qualifiedInterfaceName.Substring(lastDot + 1)
            : startsWithGlobal ? qualifiedInterfaceName.Substring(globalPrefix.Length) : qualifiedInterfaceName;
        return qualifierPart + NamingHelpers.StripInterfacePrefix(simpleName) + "Proxy";
    }
}
