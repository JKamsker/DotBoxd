using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class ExecutionPlanIntegrityTests
{
    [Fact]
    public async Task Execute_rejects_plan_with_tampered_seal()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var tampered = new ExecutionPlan(
            plan.ModuleHash,
            plan.PlanHash,
            new ExecutionPlanSeal(new string('0', 64)),
            plan.PolicyHash,
            plan.BindingManifestHash,
            plan.Module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.ExecuteAsync(
                tampered,
                "main",
                SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-PLAN-INTEGRITY");
    }

    [Fact]
    public async Task Execute_rejects_plan_with_tampered_binding_references()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var tampered = new ExecutionPlan(
            plan.ModuleHash,
            plan.PlanHash,
            plan.PlanSeal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            plan.Module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            new Dictionary<string, IReadOnlySet<string>>
            {
                ["main"] = new HashSet<string>(["log.info"], StringComparer.Ordinal)
            });

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.ExecuteAsync(
                tampered,
                "main",
                SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-PLAN-INTEGRITY");
    }
}
