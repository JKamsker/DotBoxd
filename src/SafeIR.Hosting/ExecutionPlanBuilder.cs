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
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        var moduleHash = CanonicalModuleHasher.Hash(module);
        var planHash = Hash("plan-v1", moduleHash, policy.Hash, bindings.ManifestHash);
        return new ExecutionPlan(
            moduleHash,
            planHash,
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
}
