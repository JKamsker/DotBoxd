namespace DotBoxd.Kernels.Compiler;

using System.Security.Cryptography;
using System.Text;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Verifier;

public static class CacheKeyBuilder
{
    public const string CompilerVersion = "dotboxd-compiler-9";
    public const string TypeSystemVersion = "dotboxd-type-system-2";
    public const string EffectAnalysisVersion = "dotboxd-effect-analysis-3";
    public const string CanonicalizerVersion = CanonicalModuleHasher.CanonicalizerVersion;
    public const string TargetFramework = "net10.0";

    public static string LanguageVersion => SandboxLanguage.CurrentVersionText;

    public static string RuntimeFacadeHash => VerificationPolicy.BoxedValueDefaults().RuntimeFacadeHash;

    public static string Build(ExecutionPlan plan, string entrypoint, VerificationPolicy policy, bool optimize)
    {
        var parts = new[] {
            "dotboxd-cache-v1",
            plan.ModuleHash,
            CanonicalizerVersion,
            entrypoint,
            LanguageVersion,
            CompilerVersion,
            TypeSystemVersion,
            EffectAnalysisVersion,
            policy.VerifierVersion,
            policy.AllowlistHash,
            policy.RuntimeFacadeHash,
            plan.BindingManifestHash,
            plan.PolicyHash,
            TargetFramework,
            optimize ? "opt" : "boxed-values",
            plan.Policy.Deterministic ? "deterministic" : "nondeterministic"
        };

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
    }

    public static VerificationManifestIdentity BuildManifestIdentity(
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy,
        bool optimize)
        => new(
            1,
            Build(plan, entrypoint, policy, optimize),
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            policy.RuntimeFacadeHash,
            CompilerVersion,
            TypeSystemVersion,
            EffectAnalysisVersion,
            policy.VerifierVersion,
            LanguageVersion,
            TargetFramework,
            [optimize ? "opt" : "boxed-values"]);
}
