namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Validation;

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

    public static void EnsurePrepared(ExecutionPlan plan, BindingRegistry hostBindings, byte[] planSigningKey)
    {
        ArgumentNullException.ThrowIfNull(plan);
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
            var expected = ExecutionPlanBuilder.Build(plan.Module, plan.Policy, hostBindings, validation.Functions, planSigningKey);
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
            !SameAnalysis(plan.FunctionAnalysis, expected.FunctionAnalysis))
        {
            diagnostics.Add(new SandboxDiagnostic("E-PLAN-INTEGRITY", "execution plan does not match validated module, policy, and bindings"));
        }
    }

    private static bool SameAnalysis(
        IReadOnlyDictionary<string, FunctionAnalysis> left,
        IReadOnlyDictionary<string, FunctionAnalysis> right)
        => left.Count == right.Count &&
           left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);
}
