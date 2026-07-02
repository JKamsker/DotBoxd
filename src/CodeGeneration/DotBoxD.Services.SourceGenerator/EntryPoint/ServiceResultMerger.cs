using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.EntryPoint;

internal static class ServiceResultMerger
{
    public static IEnumerable<ServiceResult> Merge(
        ImmutableArray<ServiceResult> canonicalResults,
        ImmutableArray<ServiceResult> legacyResults,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in canonicalResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TrackServiceResult(seen, result))
            {
                yield return result;
            }
        }

        foreach (var result in legacyResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TrackServiceResult(seen, result))
            {
                yield return result;
            }
        }
    }

    private static bool TrackServiceResult(HashSet<string> seen, ServiceResult result)
        => string.IsNullOrEmpty(result.QualifiedInterfaceName) ||
           seen.Add(result.QualifiedInterfaceName);
}
