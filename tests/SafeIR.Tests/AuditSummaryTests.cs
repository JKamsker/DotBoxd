namespace SafeIR.Tests;

public sealed class AuditSummaryTests
{
    [Fact]
    public async Task Interpreted_run_summary_includes_execution_hashes()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        AssertSummaryContainsExecutionHashes(summary, plan);
        Assert.Contains("mode=interpreted", summary.Message!);
        Assert.Contains("cacheStatus=None", summary.Message!);
        Assert.Equal("Interpreted", summary.Fields!["mode"]);
        Assert.Equal(plan.PlanHash, summary.Fields["planHash"]);
        Assert.Equal("None", summary.Fields["cacheStatus"]);
        Assert.Equal("1000", summary.Fields["maxFuel"]);
    }

    [Fact]
    public async Task Compiled_run_summary_includes_cache_and_execution_hashes()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        AssertSummaryContainsExecutionHashes(summary, plan);
        Assert.Contains("mode=compiled", summary.Message!);
        Assert.Contains("cacheKey=", summary.Message!);
        Assert.Contains($"artifact={result.ArtifactHash}", summary.Message!);
        Assert.Equal("Compiled", summary.Fields!["mode"]);
        Assert.Equal(plan.PolicyHash, summary.Fields["policyHash"]);
        Assert.Equal(result.ArtifactHash, summary.Fields["artifactHash"]);
        Assert.Equal("LoadedAssembly", summary.Fields["runtimeForm"]);
    }

    private static void AssertSummaryContainsExecutionHashes(SandboxAuditEvent summary, ExecutionPlan plan)
    {
        Assert.Contains($"plan={plan.PlanHash}", summary.Message!);
        Assert.Contains($"policy={plan.PolicyHash}", summary.Message!);
        Assert.Contains($"bindings={plan.BindingManifestHash}", summary.Message!);
    }
}
