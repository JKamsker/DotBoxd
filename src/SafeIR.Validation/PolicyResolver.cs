namespace SafeIR.Validation;

using SafeIR;

internal static class PolicyResolver
{
    public static void Validate(
        SandboxModule module,
        SandboxPolicy? policy,
        IReadOnlyDictionary<string, FunctionAnalysis> functions,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        if (policy is null) {
            return;
        }

        foreach (var request in module.CapabilityRequests) {
            if (!policy.GrantsCapability(request.Id)) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"requested capability '{request.Id}' is not granted"));
            }
        }

        foreach (var capability in requiredCapabilities) {
            if (!policy.GrantsCapability(capability)) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"required capability '{capability}' is not granted"));
            }
        }

        var requiredEffects = functions.Values.Aggregate(SandboxEffect.None, (current, next) => current | next.Effects);
        var deniedEffects = requiredEffects & ~policy.AllowedEffects;
        if (deniedEffects != SandboxEffect.None) {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-EFFECT", $"policy denies effects {deniedEffects}"));
        }

        if (policy.Deterministic) {
            if ((requiredEffects & SandboxEffect.Time) != 0 && policy.LogicalNow is null) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-DETERMINISM", "deterministic policy requires logical time for Time effects"));
            }

            if ((requiredEffects & SandboxEffect.Random) != 0 && policy.RandomSeed is null) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-DETERMINISM", "deterministic policy requires a random seed for Random effects"));
            }

            if ((requiredEffects & SandboxEffect.Network) != 0) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-DETERMINISM", "deterministic policy denies Network effects"));
            }
        }
    }
}
