using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Validation;

using DotBoxD.Kernels;

internal static class PolicyGrantValidator
{
    private static readonly string[] NoAllowedParameterKeys = [];

    public static void Validate(
        SandboxPolicy policy,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        var now = policy.GrantClock;
        AddDuplicateActiveGrantDiagnostics(policy.Grants, now, diagnostics);
        foreach (var grant in policy.Grants)
        {
            if (IsActive(grant, now))
            {
                ValidateGrant(grant, bindings, requiredCapabilities, requestedCapabilities, diagnostics);
            }
        }
    }

    private static bool IsActive(CapabilityGrant grant, DateTimeOffset now)
        => grant.ExpiresAt is null || grant.ExpiresAt > now;

    private static void AddDuplicateActiveGrantDiagnostics(
        IReadOnlyList<CapabilityGrant> grants,
        DateTimeOffset now,
        List<SandboxDiagnostic> diagnostics)
    {
        if (grants.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(grants.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (IsActive(grant, now))
            {
                IncrementCount(counts, grant.Id, ref nullCount);
            }
        }

        var reportedNull = false;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (IsActive(grant, now) &&
                ShouldReportDuplicate(counts, grant.Id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"capability '{grant.Id}' has multiple active grants"));
            }
        }
    }

    private static void IncrementCount(Dictionary<string, int> counts, string? value, ref int nullCount)
    {
        if (value is null)
        {
            nullCount++;
            return;
        }

        counts.TryGetValue(value, out var count);
        counts[value] = count + 1;
    }

    private static bool ShouldReportDuplicate(
        Dictionary<string, int> counts,
        string? value,
        int nullCount,
        ref bool reportedNull)
    {
        if (value is null)
        {
            if (nullCount < 2 || reportedNull)
            {
                return false;
            }

            reportedNull = true;
            return true;
        }

        if (!counts.TryGetValue(value, out var count) || count < 2)
        {
            return false;
        }

        counts[value] = 0;
        return true;
    }

    private static void ValidateGrant(
        CapabilityGrant grant,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        if (CapabilityPattern.IsWildcard(grant.Id))
        {
            ValidateWildcardGrant(grant, bindings, requiredCapabilities, requestedCapabilities, diagnostics);
            return;
        }

        if (grant.Id.StartsWith("event.read.", StringComparison.Ordinal))
        {
            ValidateEventReadGrant(grant, requiredCapabilities, requestedCapabilities, diagnostics);
            return;
        }

        if (ValidateConcreteGrant(grant.Id, grant, bindings, diagnostics))
        {
            return;
        }

        if (!requiredCapabilities.Contains(grant.Id))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                $"grant '{grant.Id}' is not supported by the prepared module"));
        }
    }

    private static void ValidateEventReadGrant(
        CapabilityGrant grant,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
        if (!requiredCapabilities.Contains(grant.Id) &&
            !ContainsRequest(requestedCapabilities, grant.Id))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                $"grant '{grant.Id}' is not supported by the prepared module"));
        }
    }

    private static bool ContainsRequest(IReadOnlyList<CapabilityRequest> requests, string capabilityId)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            if (string.Equals(requests[i].Id, capabilityId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateWildcardGrant(
        CapabilityGrant grant,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyList<CapabilityRequest> requestedCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        var matched = false;
        foreach (var required in requiredCapabilities)
        {
            if (!CapabilityPattern.Matches(grant.Id, required))
            {
                continue;
            }

            matched = true;
            ValidateConcreteGrant(required, grant, bindings, diagnostics);
        }

        foreach (var request in requestedCapabilities)
        {
            if (requiredCapabilities.Contains(request.Id) ||
                !CapabilityPattern.Matches(grant.Id, request.Id))
            {
                continue;
            }

            matched = true;
            if (!ValidateConcreteGrant(request.Id, grant, bindings, diagnostics))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"wildcard grant '{grant.Id}' matches requested capability '{request.Id}', but that capability is not supported by the prepared module"));
            }
        }

        if (!matched)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT",
                $"grant '{grant.Id}' is not supported by the prepared module"));
        }
    }

    private static bool ValidateConcreteGrant(
        string capabilityId,
        CapabilityGrant grant,
        IBindingCatalog bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (capabilityId)
        {
            case "file.read":
                FilePolicyGrantValidator.ValidateRead(grant, diagnostics);
                return true;
            case "file.write":
                FilePolicyGrantValidator.ValidateWrite(grant, diagnostics);
                return true;
            case "time.now" or "random" or "log.write" or RuntimeCapabilityIds.Async:
                RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                return true;
            case RuntimeCapabilityIds.Reentrant:
                RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"grant '{RuntimeCapabilityIds.Reentrant}' is not supported until intra-kernel reentrancy ships"));
                return true;
            default:
                if (capabilityId.StartsWith("event.read.", StringComparison.Ordinal))
                {
                    RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                    return true;
                }

                if (bindings.TryGetCapabilityGrantValidator(capabilityId, out var validator))
                {
                    validator(grant, diagnostics);
                    return true;
                }

                return false;
        }
    }

    private static void RequireAllowedKeys(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlyList<string> allowedKeys)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            if (!ContainsKey(allowedKeys, key))
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }
    }

    private static bool ContainsKey(IReadOnlyList<string> allowedKeys, string key)
    {
        for (var i = 0; i < allowedKeys.Count; i++)
        {
            if (string.Equals(allowedKeys[i], key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(List<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));
}
