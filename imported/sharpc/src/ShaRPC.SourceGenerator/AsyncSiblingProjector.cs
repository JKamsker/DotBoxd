using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class AsyncSiblingProjector
{
    public static (EquatableArray<AsyncSiblingMethod> Siblings, EquatableArray<MethodDiagnostic> Collisions)
        Compute(ServiceModel service, CancellationToken ct = default)
    {
        return Compute(service, EquatableArray<DiagnosticLocation>.Empty, ct);
    }

    public static (EquatableArray<AsyncSiblingMethod> Siblings, EquatableArray<MethodDiagnostic> Collisions)
        Compute(ServiceModel service, EquatableArray<DiagnosticLocation> methodLocations, CancellationToken ct)
    {
        var candidates = new List<AsyncSiblingMethod>();
        var collisions = new List<MethodDiagnostic>();
        var blockedSignatures = UnsupportedOriginalSignatures(service, ct);
        var originalSignatures = OriginalSignatures(service, ct);

        for (var i = 0; i < service.Methods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = service.Methods[i];
            if (m.UnsupportedReason is not null)
            {
                continue;
            }

            string siblingName = NamingHelpers.IsAsync(m.ReturnKind)
                ? m.Name
                : NamingHelpers.AsyncSiblingMethodName(m.Name);
            var siblingParameters = BuildAsyncSiblingParameters(m, ct);

            var siblingReturnKind = m.ReturnKind switch
            {
                MethodReturnKind.Void => MethodReturnKind.Task,
                MethodReturnKind.Sync => MethodReturnKind.TaskOf,
                MethodReturnKind.Stream => MethodReturnKind.TaskOfStream,
                MethodReturnKind.Pipe => MethodReturnKind.TaskOfPipe,
                _ => m.ReturnKind,
            };

            var siblingNameMatches = siblingName == m.Name;
            var signatureMatches = ParametersEqual(m.Parameters, siblingParameters, ct);
            var requiresExtra = !(siblingNameMatches && signatureMatches && NamingHelpers.IsAsync(m.ReturnKind));
            var candidateKey = SignatureKey(siblingName, m.TypeParameterCount, siblingParameters, ct);
            if (requiresExtra && blockedSignatures.TryGetValue(candidateKey, out var blockerName))
            {
                collisions.Add(new MethodDiagnostic(
                    service.InterfaceName,
                    m.Name,
                    $"the async-sibling projection '{siblingName}' would collide with unsupported method '{blockerName}'. Rename one of the methods or drop the trailing 'Async' on the sync method.",
                    GetLocation(i, methodLocations)));
                continue;
            }

            if (requiresExtra && originalSignatures.TryGetValue(candidateKey, out blockerName))
            {
                collisions.Add(new MethodDiagnostic(
                    service.InterfaceName,
                    m.Name,
                    $"the async-sibling projection '{siblingName}' would collide with method '{blockerName}'. Rename one of the methods or drop the trailing 'Async' on the sync method.",
                    GetLocation(i, methodLocations)));
                continue;
            }

            candidates.Add(new AsyncSiblingMethod(
                i,
                siblingName,
                m,
                siblingReturnKind,
                siblingParameters,
                requiresExtra));
        }

        var groups = new Dictionary<string, List<AsyncSiblingMethod>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = SignatureKey(candidate.Name, candidate.Source.TypeParameterCount, candidate.Parameters, ct);
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
            var key = SignatureKey(candidate.Name, candidate.Source.TypeParameterCount, candidate.Parameters, ct);
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
                ct.ThrowIfCancellationRequested();

                if (ReferenceEquals(row, keeper))
                {
                    continue;
                }

                var other = group.First(candidateRow => !ReferenceEquals(candidateRow, row));
                collisions.Add(new MethodDiagnostic(
                    service.InterfaceName,
                    row.Source.Name,
                    $"the async-sibling projection '{row.Name}' would collide with '{other.Source.Name}'. Rename one of the methods or drop the trailing 'Async' on the sync method.",
                    GetLocation(row.SourceIndex, methodLocations)));
            }
        }

        return (rows.ToEquatableArray(), collisions.ToEquatableArray());
    }

    private static DiagnosticLocation GetLocation(
        int sourceIndex,
        EquatableArray<DiagnosticLocation> methodLocations)
    {
        if (sourceIndex < 0 || sourceIndex >= methodLocations.Count)
        {
            return default;
        }

        return methodLocations[sourceIndex];
    }

    private static Dictionary<string, string> UnsupportedOriginalSignatures(
        ServiceModel service,
        CancellationToken ct)
    {
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (method.UnsupportedReason is not null)
            {
                signatures[SignatureKey(method.Name, method.TypeParameterCount, method.Parameters, ct)] = method.Name;
            }
        }

        return signatures;
    }

    private static Dictionary<string, string> OriginalSignatures(
        ServiceModel service,
        CancellationToken ct)
    {
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            signatures[SignatureKey(method.Name, method.TypeParameterCount, method.Parameters, ct)] = method.Name;
        }

        return signatures;
    }

    private static string SignatureKey(
        string methodName,
        int arity,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct) =>
        MethodSignatureFacts.GetSignatureKey(methodName, arity, parameters, ct);

    private static EquatableArray<ParameterModel> BuildAsyncSiblingParameters(
        MethodModel method,
        CancellationToken ct)
    {
        if (NamingHelpers.IsAsync(method.ReturnKind) && method.HasCancellationToken)
        {
            return method.Parameters;
        }

        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (!parameter.IsCancellationToken)
            {
                parameters.Add(parameter);
            }
        }

        parameters.Add(new ParameterModel(
            UniqueParameterName(method.Parameters, "ct", ct),
            "global::System.Threading.CancellationToken",
            "global::System.Threading.CancellationToken",
            IsCancellationToken: true,
            HasDefaultValue: true,
            MetadataType: "global::System.Threading.CancellationToken"));

        return parameters.ToEquatableArray();
    }

    private static string UniqueParameterName(
        EquatableArray<ParameterModel> parameters,
        string baseName,
        CancellationToken ct)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
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

    private static bool ParametersEqual(
        EquatableArray<ParameterModel> left,
        EquatableArray<ParameterModel> right,
        CancellationToken ct)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
