namespace SafeIR.Validation.Internal;

using SafeIR;

internal static class PolicyResolver
{
    public static void Validate(
        SandboxModule module,
        IBindingCatalog bindings,
        SandboxPolicy? policy,
        SandboxEffect requiredEffects,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        if (policy is null) {
            return;
        }

        if (!policy.AllowedEffects.ContainsOnlyKnownBits()) {
            diagnostics.Add(new SandboxDiagnostic("E-POLICY-EFFECT", "policy declares unknown effects"));
        }

        PolicyGrantValidator.Validate(policy, bindings, requiredCapabilities, diagnostics);

        // Capture the grant clock once so every membership probe in this validation pass
        // shares a single consistent snapshot instead of re-reading DateTimeOffset.UtcNow
        // per requested/required capability over the cached grant index.
        var now = policy.GrantClock;

        foreach (var request in module.CapabilityRequests) {
            if (!policy.GrantsCapability(request.Id, now)) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"requested capability '{request.Id}' is not granted"));
            }
        }

        foreach (var capability in requiredCapabilities) {
            if (!policy.GrantsCapability(capability, now)) {
                diagnostics.Add(new SandboxDiagnostic("E-POLICY-CAP", $"required capability '{capability}' is not granted"));
            }
        }

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

            var externalEffects = ExternalEffects(requiredEffects | policy.AllowedEffects);
            if (externalEffects != SandboxEffect.None) {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-DETERMINISM",
                    $"deterministic policy denies external effects {externalEffects}"));
            }
        }
    }

    private static SandboxEffect ExternalEffects(SandboxEffect effects)
        => effects & (
            SandboxEffect.FileRead |
            SandboxEffect.FileWrite |
            SandboxEffect.Network |
            SandboxEffect.HostStateRead |
            SandboxEffect.HostStateWrite);
}
