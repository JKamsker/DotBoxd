namespace SafeIR.Hosting;

using System.Collections.Concurrent;
using SafeIR;
using SafeIR.Runtime;

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
            if (plan.Bindings.TryGet(bindingId, out var binding) &&
                binding.RequiredCapability is not null)
            {
                required.Add(binding.RequiredCapability);
            }
        }

        return required;
    }

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
}
