using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Hosting.Worker;

internal static class WorkerRunSummaryValidator
{
    internal static bool RunSummaryMatches(
        ExecutionPlan plan,
        SandboxExecutionResult result,
        SandboxAuditEvent summary)
    {
        if (summary.Fields is null ||
            !FieldEquals(summary, "mode", result.ActualMode.ToString()) ||
            !FieldEquals(summary, "executionMode", result.ActualMode.ToString()) ||
            !FieldEquals(summary, "executionDispatched", true) ||
            !HasNonEmptyField(summary, "cacheStatus") ||
            !FieldEquals(summary, "moduleHash", plan.ModuleHash) ||
            !FieldEquals(summary, "planHash", plan.PlanHash) ||
            !FieldEquals(summary, "policyId", ExpectedPolicyId(plan)) ||
            !FieldEquals(summary, "policyHash", plan.PolicyHash) ||
            !FieldEquals(summary, "bindingManifestHash", plan.BindingManifestHash) ||
            !FieldEquals(summary, "fuelUsed", result.ResourceUsage.FuelUsed) ||
            !FieldEquals(summary, "loopIterations", result.ResourceUsage.LoopIterations) ||
            !FieldEquals(summary, "allocatedBytes", result.ResourceUsage.AllocatedBytes) ||
            !FieldEquals(summary, "allocationCharged", result.ResourceUsage.AllocatedBytes) ||
            !FieldEquals(summary, "hostCalls", result.ResourceUsage.HostCalls) ||
            !FieldEquals(summary, "fileBytesRead", result.ResourceUsage.FileBytesRead) ||
            !FieldEquals(summary, "fileBytesWritten", result.ResourceUsage.FileBytesWritten) ||
            !FieldEquals(summary, "networkBytesRead", result.ResourceUsage.NetworkBytesRead) ||
            !FieldEquals(summary, "networkBytesWritten", result.ResourceUsage.NetworkBytesWritten) ||
            !FieldEquals(summary, "logEvents", result.ResourceUsage.LogEvents) ||
            !FieldEquals(summary, "collectionElements", result.ResourceUsage.CollectionElements) ||
            !FieldEquals(summary, "stringBytes", result.ResourceUsage.StringBytes))
        {
            return false;
        }

        if (!WorkerEnvelopeValidators.BudgetFieldsMatch(plan, summary))
        {
            return false;
        }

        if (result.ActualMode != ExecutionMode.Compiled)
        {
            return !summary.Fields.ContainsKey("artifactHash") &&
                   !summary.Fields.ContainsKey("runtimeForm") &&
                   !summary.Fields.ContainsKey("cacheKey");
        }

        if (!result.Succeeded)
        {
            return FailedCompiledEnvelopeMatches(result, summary);
        }

        if (!IsHexSha256(result.ArtifactHash))
        {
            return false;
        }

        var artifactHash = result.ArtifactHash!;
        return FieldEquals(summary, "artifactHash", artifactHash) &&
               FieldEquals(summary, "runtimeForm", "LoadedAssembly") &&
               HasHexSha256Field(summary, "cacheKey");
    }

    private static bool FailedCompiledEnvelopeMatches(SandboxExecutionResult result, SandboxAuditEvent summary)
    {
        var hasResultArtifact = !string.IsNullOrWhiteSpace(result.ArtifactHash);
        if (!hasResultArtifact)
        {
            return !summary.Fields!.ContainsKey("artifactHash") &&
                   !summary.Fields.ContainsKey("runtimeForm") &&
                   !summary.Fields.ContainsKey("cacheKey");
        }

        return IsHexSha256(result.ArtifactHash) &&
               FieldEquals(summary, "artifactHash", result.ArtifactHash!) &&
               FieldEquals(summary, "runtimeForm", "LoadedAssembly") &&
               HasHexSha256Field(summary, "cacheKey");
    }

    private static bool FieldEquals(SandboxAuditEvent summary, string key, string value)
        => summary.Fields!.TryGetValue(key, out var actual) &&
           string.Equals(actual, value, StringComparison.Ordinal);

    private static bool FieldEquals(SandboxAuditEvent summary, string key, long value)
        => FieldEquals(summary, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static bool FieldEquals(SandboxAuditEvent summary, string key, bool value)
        => FieldEquals(summary, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static bool HasNonEmptyField(SandboxAuditEvent summary, string key)
        => summary.Fields!.TryGetValue(key, out var value) &&
           !string.IsNullOrWhiteSpace(value);

    private static bool HasHexSha256Field(SandboxAuditEvent summary, string key)
        => summary.Fields!.TryGetValue(key, out var value) && IsHexSha256(value);

    private static string ExpectedPolicyId(ExecutionPlan plan)
        => RunSummaryAuditFields.SafePolicyId(plan.Policy.PolicyId);

    internal static bool IsHexSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
