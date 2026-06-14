using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace DotBoxd.Services.SourceGenerator;

internal static class FinalRejectedServiceResolver
{
    public static RejectedServiceIndex Resolve(
        ImmutableArray<FinalRejectionInput> baseResults,
        CancellationToken ct)
    {
        var rejected = CreateRejectedServices(baseResults, ct);
        var seen = new List<RejectedServiceIndex>();

        for (var iteration = 0; iteration <= baseResults.Length + 1; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            var cycleStart = IndexOf(seen, rejected, ct);
            if (cycleStart >= 0)
            {
                return Union(seen, cycleStart, rejected, ct);
            }

            seen.Add(rejected);
            var next = ComputeNext(baseResults, rejected, ct);
            if (SameRejectedServices(rejected, next, ct))
            {
                return next;
            }

            rejected = next;
        }

        return rejected;
    }

    private static RejectedServiceIndex ComputeNext(
        ImmutableArray<FinalRejectionInput> baseResults,
        RejectedServiceIndex rejected,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<RejectedServiceIdentity>();
        foreach (var input in baseResults)
        {
            ct.ThrowIfCancellationRequested();

            if (IsRejectedAfterAsyncSiblingCollision(input, rejected, ct))
            {
                builder.Add(new RejectedServiceIdentity(input.QualifiedInterfaceName));
            }
        }

        return RejectedServiceIndex.Create(builder.ToImmutable(), ct);
    }

    private static RejectedServiceIndex CreateRejectedServices(
        ImmutableArray<FinalRejectionInput> results,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<RejectedServiceIdentity>();
        foreach (var input in results)
        {
            ct.ThrowIfCancellationRequested();

            if (input.IsRejected)
            {
                builder.Add(new RejectedServiceIdentity(input.QualifiedInterfaceName));
            }
        }

        return RejectedServiceIndex.Create(builder.ToImmutable(), ct);
    }

    private static bool IsRejectedAfterAsyncSiblingCollision(
        FinalRejectionInput input,
        RejectedServiceIndex rejected,
        CancellationToken ct)
    {
        if (input.IsRejected || input.Methods.IsEmpty)
        {
            return input.IsRejected;
        }

        return WillGenerateAsyncSiblingInterface(input, rejected, ct);
    }

    private static bool WillGenerateAsyncSiblingInterface(
        FinalRejectionInput input,
        RejectedServiceIndex rejected,
        CancellationToken ct)
    {
        var blockedSignatures = new HashSet<string>(System.StringComparer.Ordinal);
        var originalSignatures = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var method in input.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            originalSignatures.Add(method.OriginalSignatureKey);
            if (IsUnsupported(method, rejected, ct))
            {
                blockedSignatures.Add(method.OriginalSignatureKey);
            }
        }

        var candidateCounts = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var candidateHasReusableOriginal = new Dictionary<string, bool>(System.StringComparer.Ordinal);
        foreach (var method in input.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (IsUnsupported(method, rejected, ct) ||
                method.RequiresExtraProxyMethod &&
                (blockedSignatures.Contains(method.CandidateSignatureKey) ||
                    originalSignatures.Contains(method.CandidateSignatureKey)))
            {
                continue;
            }

            candidateCounts.TryGetValue(method.CandidateSignatureKey, out var count);
            candidateCounts[method.CandidateSignatureKey] = count + 1;
            candidateHasReusableOriginal[method.CandidateSignatureKey] =
                candidateHasReusableOriginal.TryGetValue(method.CandidateSignatureKey, out var hasReusable) &&
                hasReusable ||
                !method.RequiresExtraProxyMethod;
        }

        foreach (var entry in candidateCounts)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Value == 1 ||
                candidateHasReusableOriginal.TryGetValue(entry.Key, out var hasReusable) && hasReusable)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsupported(
        FinalRejectionMethod method,
        RejectedServiceIndex rejected,
        CancellationToken ct) =>
        method.IsUnsupported ||
        method.SubServiceQualifiedInterfaceName is not null &&
        rejected.Contains(method.SubServiceQualifiedInterfaceName, ct);

    private static int IndexOf(
        List<RejectedServiceIndex> seen,
        RejectedServiceIndex target,
        CancellationToken ct)
    {
        for (var i = 0; i < seen.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (SameRejectedServices(seen[i], target, ct))
            {
                return i;
            }
        }

        return -1;
    }

    private static RejectedServiceIndex Union(
        List<RejectedServiceIndex> seen,
        int cycleStart,
        RejectedServiceIndex repeated,
        CancellationToken ct)
    {
        var identities = ImmutableArray.CreateBuilder<RejectedServiceIdentity>();
        for (var i = cycleStart; i < seen.Count; i++)
        {
            AddAll(identities, seen[i], ct);
        }

        AddAll(identities, repeated, ct);
        return RejectedServiceIndex.Create(identities.ToImmutable(), ct);
    }

    private static void AddAll(
        ImmutableArray<RejectedServiceIdentity>.Builder identities,
        RejectedServiceIndex index,
        CancellationToken ct)
    {
        foreach (var qualifiedName in index.QualifiedInterfaceNames.Array)
        {
            ct.ThrowIfCancellationRequested();
            identities.Add(new RejectedServiceIdentity(qualifiedName));
        }
    }

    private static bool SameRejectedServices(
        RejectedServiceIndex left,
        RejectedServiceIndex right,
        CancellationToken ct)
    {
        if (left.QualifiedInterfaceNames.Count != right.QualifiedInterfaceNames.Count)
        {
            return false;
        }

        for (var i = 0; i < left.QualifiedInterfaceNames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (left.QualifiedInterfaceNames[i] != right.QualifiedInterfaceNames[i])
            {
                return false;
            }
        }

        return true;
    }
}
