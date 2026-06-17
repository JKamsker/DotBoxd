using System.Collections.Concurrent;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost
{
    private readonly ConcurrentDictionary<string, RevokedCapability> _revokedCapabilities =
        new(StringComparer.Ordinal);

    public void RevokeCapability(string capabilityId, string reason = "")
    {
        ValidateCapabilityId(capabilityId);
        _revokedCapabilities[capabilityId] = new RevokedCapability(
            capabilityId,
            SanitizeReason(reason),
            DateTimeOffset.UtcNow);
    }

    private bool TryGetRevokedCapability(
        ExecutionPlan plan,
        string entrypoint,
        out RevokedCapability revoked)
    {
        if (_revokedCapabilities.IsEmpty)
        {
            revoked = null!;
            return false;
        }

        foreach (var capabilityId in RequiredCapabilities(plan, entrypoint))
        {
            if (_revokedCapabilities.TryGetValue(capabilityId, out revoked!))
            {
                return true;
            }
        }

        revoked = null!;
        return false;
    }

    private bool TryGetCapabilityDenial(
        ExecutionPlan plan,
        string entrypoint,
        out CapabilityDenial denial)
    {
        if (TryGetRevokedCapability(plan, entrypoint, out var revoked))
        {
            denial = new RevokedCapabilityDenial(revoked);
            return true;
        }

        var now = plan.Policy.GrantClock;
        foreach (var capabilityId in RequiredCapabilities(plan, entrypoint))
        {
            if (!plan.Policy.GrantsCapability(capabilityId, now))
            {
                denial = new UnavailableCapabilityDenial(new UnavailableCapability(capabilityId, now));
                return true;
            }
        }

        denial = null!;
        return false;
    }

    private static IEnumerable<string> RequiredCapabilities(ExecutionPlan plan, string entrypoint)
    {
        var required = new HashSet<string>(
            plan.Module.CapabilityRequests.Select(request => request.Id),
            StringComparer.Ordinal);

        if (!plan.BindingReferences.TryGetValue(entrypoint, out var bindingReferences))
        {
            return required;
        }

        foreach (var bindingId in bindingReferences)
        {
            if (!plan.Bindings.TryGet(bindingId, out var binding))
            {
                continue;
            }

            if (binding.RequiredCapability is not null)
            {
                required.Add(binding.RequiredCapability);
            }

            if (binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0)
            {
                required.Add(RuntimeCapabilityIds.Async);
            }
        }

        return required;
    }

    private static bool EntrypointHasAsyncBinding(ExecutionPlan plan, string entrypoint)
    {
        if (!plan.BindingReferences.TryGetValue(entrypoint, out var bindingReferences))
        {
            return false;
        }

        foreach (var bindingId in bindingReferences)
        {
            if (plan.Bindings.TryGet(bindingId, out var binding) && binding.IsAsync)
            {
                return true;
            }
        }

        return false;
    }

    private static bool EntrypointHasHostBinding(ExecutionPlan plan, string entrypoint)
        => plan.BindingReferences.TryGetValue(entrypoint, out var bindingReferences) &&
           bindingReferences.Count > 0;

    private static bool ShouldUseCompiledAsyncWorker(ExecutionPlan plan, string entrypoint)
        => plan.Policy.GrantsCapability(RuntimeCapabilityIds.Async) &&
           EntrypointHasAsyncBinding(plan, entrypoint);

    private static bool ShouldUseCompiledInlineAwaitPump(ExecutionPlan plan, string entrypoint)
        => plan.Policy.GrantsCapability(RuntimeCapabilityIds.Async) &&
           !EntrypointHasAsyncBinding(plan, entrypoint) &&
           EntrypointHasHostBinding(plan, entrypoint);

    private static void ValidateCapabilityId(string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId) ||
            capabilityId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "capability id must be non-empty and must not contain control characters",
                nameof(capabilityId));
        }
    }

    private static string SanitizeReason(string reason)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length == 0)
        {
            return "revoked by host";
        }

        var sanitized = new string(trimmed
            .Select(c => char.IsControl(c) ? ' ' : c)
            .ToArray());
        sanitized = AuditTextSanitizer.SanitizeAndRedact(sanitized);
        return sanitized.Length <= 256 ? sanitized : sanitized[..256];
    }

    private sealed record RevokedCapability(string Id, string Reason, DateTimeOffset RevokedAt);

    private sealed record UnavailableCapability(string Id, DateTimeOffset CheckedAt);

    private abstract record CapabilityDenial;

    private sealed record RevokedCapabilityDenial(RevokedCapability Revoked) : CapabilityDenial;

    private sealed record UnavailableCapabilityDenial(UnavailableCapability Unavailable) : CapabilityDenial;
}
