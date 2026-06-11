namespace SafeIR.Compiler;

using System.Security.Cryptography;
using System.Text;
using SafeIR;
using SafeIR.Verifier;

public static class CacheKeyBuilder
{
    public const string CompilerVersion = "safe-ir-compiler-5";
    public const string RuntimeFacadeHash = "safe-ir-runtime-facade-5";
    public const string TargetFramework = "net10.0";

    public static string LanguageVersion => SandboxLanguage.CurrentVersionText;

    public static string Build(ExecutionPlan plan, string entrypoint, VerificationPolicy policy, bool optimize)
    {
        var parts = new[] {
            "safe-ir-cache-v1",
            plan.ModuleHash,
            entrypoint,
            LanguageVersion,
            CompilerVersion,
            policy.VerifierVersion,
            RuntimeFacadeHash,
            plan.BindingManifestHash,
            plan.PolicyHash,
            TargetFramework,
            optimize ? "opt" : "boxed-values",
            plan.Policy.Deterministic ? "deterministic" : "nondeterministic"
        };

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
    }
}
