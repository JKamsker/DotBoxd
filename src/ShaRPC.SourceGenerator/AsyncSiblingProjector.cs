using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class AsyncSiblingProjector
{
    public static (EquatableArray<AsyncSiblingMethod> Siblings, EquatableArray<MethodDiagnostic> Collisions)
        Compute(ServiceModel service, CancellationToken ct = default)
    {
        var candidates = new List<AsyncSiblingMethod>();
        var collisions = new List<MethodDiagnostic>();

        foreach (var m in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();
            if (m.UnsupportedReason is not null)
            {
                continue;
            }

            string siblingName = NamingHelpers.IsAsync(m.ReturnKind)
                ? m.Name
                : NamingHelpers.AsyncSiblingMethodName(m.Name);
            var siblingParameters = BuildAsyncSiblingParameters(m);

            var siblingReturnKind = m.ReturnKind switch
            {
                MethodReturnKind.Void => MethodReturnKind.Task,
                MethodReturnKind.Sync => MethodReturnKind.TaskOf,
                _ => m.ReturnKind,
            };

            var siblingNameMatches = siblingName == m.Name;
            var signatureMatches = ParametersEqual(m.Parameters, siblingParameters);
            var requiresExtra = !(siblingNameMatches && signatureMatches && NamingHelpers.IsAsync(m.ReturnKind));

            candidates.Add(new AsyncSiblingMethod(siblingName, m, siblingReturnKind, siblingParameters, requiresExtra));
        }

        var groups = new Dictionary<string, List<AsyncSiblingMethod>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = SignatureKey(candidate);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<AsyncSiblingMethod>();
                groups[key] = group;
            }
            group.Add(candidate);
        }

        var rows = new List<AsyncSiblingMethod>();
        var handledKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = SignatureKey(candidate);
            if (!handledKeys.Add(key))
            {
                continue;
            }

            var group = groups[key];
            if (group.Count == 1)
            {
                rows.Add(candidate);
                continue;
            }

            var keeper = group.FirstOrDefault(static row => !row.RequiresExtraProxyMethod);
            if (keeper is not null)
            {
                rows.Add(keeper);
            }

            foreach (var row in group)
            {
                if (ReferenceEquals(row, keeper))
                {
                    continue;
                }

                var other = group.First(candidateRow => !ReferenceEquals(candidateRow, row));
                collisions.Add(new MethodDiagnostic(
                    service.InterfaceName,
                    row.Source.Name,
                    $"the async-sibling projection '{row.Name}' would collide with '{other.Source.Name}'. Rename one of the methods or drop the trailing 'Async' on the sync method."));
            }
        }

        return (rows.ToEquatableArray(), collisions.ToEquatableArray());
    }

    private static string SignatureKey(AsyncSiblingMethod method) =>
        method.Name + "(" +
        string.Join(",", method.Parameters.Array.Select(p => p.RefKindKeyword + p.Type)) + ")";

    private static EquatableArray<ParameterModel> BuildAsyncSiblingParameters(MethodModel method)
    {
        if (NamingHelpers.IsAsync(method.ReturnKind) && method.HasCancellationToken)
        {
            return method.Parameters;
        }

        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters.Array)
        {
            if (!parameter.IsCancellationToken)
            {
                parameters.Add(parameter);
            }
        }

        parameters.Add(new ParameterModel(
            UniqueParameterName(method.Parameters, "ct"),
            "global::System.Threading.CancellationToken",
            IsCancellationToken: true,
            HasDefaultValue: true));

        return parameters.ToEquatableArray();
    }

    private static string UniqueParameterName(EquatableArray<ParameterModel> parameters, string baseName)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters.Array)
        {
            usedNames.Add(parameter.Name);
        }

        var candidate = baseName;
        var suffix = 1;
        while (usedNames.Contains(candidate))
        {
            candidate = baseName + suffix;
            suffix++;
        }

        return candidate;
    }

    private static bool ParametersEqual(
        EquatableArray<ParameterModel> left,
        EquatableArray<ParameterModel> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
