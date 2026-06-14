namespace SafeIR.Tests;

public sealed class AuditSummaryTests
{
    [Fact]
    public async Task Interpreted_run_summary_includes_execution_hashes()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithPolicyId("summary-policy")
            .WithFuel(1_000)
            .Build());

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
        Assert.Equal("Interpreted", summary.Fields["executionMode"]);
        Assert.Equal(plan.PlanHash, summary.Fields["planHash"]);
        Assert.Equal("summary-policy", summary.Fields["policyId"]);
        Assert.Equal("True", summary.Fields["executionDispatched"]);
        Assert.Equal("None", summary.Fields["cacheStatus"]);
        Assert.Equal(summary.Fields["allocatedBytes"], summary.Fields["allocationCharged"]);
        Assert.Equal("1000", summary.Fields["maxFuel"]);
    }

    [Fact]
    public async Task Compiled_run_summary_includes_cache_and_execution_hashes()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithPolicyId("summary-policy")
            .WithFuel(1_000)
            .Build());

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
        Assert.Equal("Compiled", summary.Fields["executionMode"]);
        Assert.Equal("summary-policy", summary.Fields["policyId"]);
        Assert.Equal(plan.PolicyHash, summary.Fields["policyHash"]);
        Assert.Equal("True", summary.Fields["executionDispatched"]);
        Assert.Equal(summary.Fields["allocatedBytes"], summary.Fields["allocationCharged"]);
        Assert.Equal(result.ArtifactHash, summary.Fields["artifactHash"]);
        Assert.Equal("LoadedAssembly", summary.Fields["runtimeForm"]);
    }

    [Fact]
    public async Task Fail_closed_run_summary_includes_required_spec_field_names()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithPolicyId("summary-policy")
            .WithFuel(1_000)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal("Auto", summary.Fields!["mode"]);
        Assert.Equal("Auto", summary.Fields["executionMode"]);
        Assert.Equal("False", summary.Fields["executionDispatched"]);
        Assert.Equal(summary.Fields["allocatedBytes"], summary.Fields["allocationCharged"]);
        Assert.Equal("summary-policy", summary.Fields["policyId"]);
    }

    [Fact]
    public async Task Run_summary_redacts_unsafe_policy_id()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithPolicyId("tenant-prod-api-key=abc123\nnext")
            .WithFuel(1_000)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal("[redacted]", summary.Fields!["policyId"]);
        Assert.Contains("policyId=[redacted]", summary.Message!);
        Assert.DoesNotContain("abc123", summary.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key", summary.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', summary.Message!);
    }

    private static void AssertSummaryContainsExecutionHashes(SandboxAuditEvent summary, ExecutionPlan plan)
    {
        Assert.Contains($"plan={plan.PlanHash}", summary.Message!);
        Assert.Contains($"policy={plan.PolicyHash}", summary.Message!);
        Assert.Contains($"policyId={summary.Fields!["policyId"]}", summary.Message!);
        Assert.Contains($"bindings={plan.BindingManifestHash}", summary.Message!);
    }
}
