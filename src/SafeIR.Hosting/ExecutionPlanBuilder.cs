namespace SafeIR.Hosting;

using System.Security.Cryptography;
using System.Text;
using SafeIR;

internal static class ExecutionPlanBuilder
{
    public static ExecutionPlan Build(
        SandboxModule module,
        SandboxPolicy policy,
        BindingRegistry bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functions,
        byte[] planSigningKey)
    {
        var moduleHash = CanonicalModuleHasher.Hash(module);
        var planHash = Hash("plan-v1", moduleHash, policy.Hash, bindings.ManifestHash);
        var planSeal = Seal(planSigningKey, moduleHash, planHash, policy, bindings.ManifestHash, functions);
        return new ExecutionPlan(
            moduleHash,
            planHash,
            new ExecutionPlanSeal(planSeal),
            policy.Hash,
            bindings.ManifestHash,
            module,
            policy,
            bindings,
            policy.ResourceLimits,
            functions);
    }

    private static string Hash(params string[] parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();

    private static string Seal(
        byte[] planSigningKey,
        string moduleHash,
        string planHash,
        SandboxPolicy policy,
        string bindingManifestHash,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        var parts = new List<string> {
            "plan-seal-v1",
            moduleHash,
            planHash,
            policy.Hash,
            bindingManifestHash,
            policy.ResourceLimits.MaxFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxAllocatedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxCallDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxHostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxListLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxMapEntries.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxCollectionDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxTotalCollectionElements.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxFileBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxFileBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxNetworkBytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxNetworkBytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxLogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxLogMessageLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxStringLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxTotalStringBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            policy.ResourceLimits.MaxWallTime?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };
        foreach (var item in functions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            parts.Add(item.Key);
            parts.Add(item.Value.ReturnType.ToString());
            parts.Add(((int)item.Value.Effects).ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(item.Value.CanReorder.ToString());
        }

        return Convert
            .ToHexString(HMACSHA256.HashData(planSigningKey, Encoding.UTF8.GetBytes(Encode(parts))))
            .ToLowerInvariant();
    }

    private static string Encode(IReadOnlyList<string> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.Length);
            builder.Append(':');
            builder.Append(part);
            builder.Append('|');
        }

        return builder.ToString();
    }
}
