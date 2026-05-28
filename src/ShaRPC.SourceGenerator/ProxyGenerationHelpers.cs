using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class ProxyGenerationHelpers
{
    public static void AppendParameterList(
        StringBuilder sb,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct = default)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0) sb.Append(", ");
            var p = parameters[i];
            sb.Append(p.RefKindKeyword).Append(p.Type).Append(' ').Append(p.Name);
            if (p.IsCancellationToken && p.HasDefaultValue)
            {
                sb.Append(" = default");
            }
        }
    }

    public static string GetCancellationTokenArgument(
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct = default)
    {
        foreach (var p in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (p.IsCancellationToken)
            {
                return p.Name;
            }
        }

        return "default";
    }

    public static List<ParameterModel> GetRequestParameters(
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct = default)
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

    public static bool MethodNameCollidesWithProxy(string methodName, string proxyName) =>
        IdentifierHelpers.UnescapeIdentifier(methodName) == proxyName;

    public static string UniqueGeneratedLocalName(
        EquatableArray<ParameterModel> parameters,
        string baseName,
        CancellationToken ct = default)
    {
        var usedNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var parameter in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();
            usedNames.Add(parameter.Name);
        }

        var candidate = baseName;
        var suffix = 1;
        while (usedNames.Contains(candidate))
        {
            ct.ThrowIfCancellationRequested();

            candidate = baseName + suffix;
            suffix++;
        }

        return candidate;
    }
}
