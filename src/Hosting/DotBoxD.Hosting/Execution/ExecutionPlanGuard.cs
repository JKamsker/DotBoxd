namespace DotBoxD.Hosting;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Validation;

internal static class ExecutionPlanGuard
{
    public static void EnsurePolicyLimits(SandboxPolicy policy)
    {
        try
        {
            ResourceLimitValidation.Validate(policy.ResourceLimits);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("E-POLICY-LIMIT", $"policy resource limit '{ex.ParamName}' must be non-negative")
            ]);
        }
    }

    public static void EnsurePrepared(
        ExecutionPlan plan,
        BindingRegistry hostBindings,
        byte[] planSigningKey,
        PreparedPlanIntegrityCache preparedPlans)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ReferenceEquals(plan.Bindings, hostBindings) &&
            preparedPlans.ContainsTrustedReference(plan))
        {
            return;
        }

        if (IsTrustedPreparedPlan(plan, hostBindings, preparedPlans))
        {
            // This host prepared, validated, and sealed this exact identity; the per-run check is
            // O(1) against the trusted entry instead of rebuilding the whole prepare pipeline.
            return;
        }

        var diagnostics = new List<SandboxDiagnostic>();
        EnsurePolicyLimits(plan.Policy, diagnostics);
        if (!ReferenceEquals(plan.Bindings, hostBindings))
        {
            diagnostics.Add(new SandboxDiagnostic("E-PLAN-BINDINGS", "execution plan was not prepared by this host"));
        }

        var validation = new ModuleValidator().Validate(plan.Module, hostBindings, plan.Policy);
        if (!validation.Succeeded)
        {
            diagnostics.AddRange(validation.Diagnostics);
        }
        else
        {
            var expected = ExecutionPlanBuilder.Build(
                plan.Module,
                plan.Policy,
                hostBindings,
                validation.Functions,
                validation.BindingReferences,
                planSigningKey);
            ComparePlan(plan, expected, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
    }

    private static void EnsurePolicyLimits(SandboxPolicy policy, List<SandboxDiagnostic> diagnostics)
    {
        try
        {
            ResourceLimitValidation.Validate(policy.ResourceLimits);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-LIMIT",
                $"policy resource limit '{ex.ParamName}' must be non-negative"));
        }
    }

    private static bool IsTrustedPreparedPlan(
        ExecutionPlan plan,
        BindingRegistry hostBindings,
        PreparedPlanIntegrityCache preparedPlans)
        => ReferenceEquals(plan.Bindings, hostBindings) &&
           preparedPlans.TryGetTrusted(plan.PlanSeal, out var trusted) &&
           MatchesTrustedIdentity(plan, trusted);

    // The seal is an authentic host HMAC (a cache hit), but a caller can still rebuild a plan that
    // reuses that seal while swapping fields. Two independent kinds of swap must be rejected:
    //   * module/policy/bindings: confirmed by reference against the exact instances this host
    //     validated and sealed (the trusted entry). A different module carrying stale copied hashes
    //     would otherwise skip the validation that surfaces, e.g., E-CALL-UNKNOWN.
    //   * function analysis / binding references: attacker-supplied metadata the constructor stores
    //     verbatim, so they are compared structurally against the trusted prepared values.
    // The hashes/seal are deterministically derived from these, so this is the full prepared identity.
    private static bool MatchesTrustedIdentity(ExecutionPlan candidate, ExecutionPlan trusted)
        => ReferenceEquals(candidate, trusted) ||
           (ReferenceEquals(candidate.Module, trusted.Module) &&
            ReferenceEquals(candidate.Policy, trusted.Policy) &&
            ReferenceEquals(candidate.Bindings, trusted.Bindings) &&
            SameAnalysis(candidate.FunctionAnalysis, trusted.FunctionAnalysis) &&
            SameBindingReferences(candidate.BindingReferences, trusted.BindingReferences));

    private static void ComparePlan(
        ExecutionPlan plan,
        ExecutionPlan expected,
        List<SandboxDiagnostic> diagnostics)
    {
        if (plan.ModuleHash != expected.ModuleHash ||
            plan.PolicyHash != expected.PolicyHash ||
            plan.BindingManifestHash != expected.BindingManifestHash ||
            plan.PlanHash != expected.PlanHash ||
            !plan.PlanSeal.Equals(expected.PlanSeal) ||
            plan.Budget != expected.Budget ||
            !SameAnalysis(plan.FunctionAnalysis, expected.FunctionAnalysis) ||
            !SameBindingReferences(plan.BindingReferences, expected.BindingReferences))
        {
            diagnostics.Add(new SandboxDiagnostic("E-PLAN-INTEGRITY", "execution plan does not match validated module, policy, and bindings"));
        }
    }

    private static bool SameAnalysis(
        IReadOnlyDictionary<string, FunctionAnalysis> left,
        IReadOnlyDictionary<string, FunctionAnalysis> right)
        => left.Count == right.Count &&
           left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);

    private static bool SameBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> left,
        IReadOnlyDictionary<string, IReadOnlySet<string>> right)
        => left.Count == right.Count &&
           left.All(item =>
               right.TryGetValue(item.Key, out var value) &&
               item.Value.SetEquals(value));
}
