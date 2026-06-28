using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Audit;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class AuditSummaryTests
{
    private const int PolicyIdAllocationIterations = 100_000;
    private const int PolicyIdAllocationWarmupIterations = 5_000;

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

    [Theory]
    [InlineData("tenant-prod-api-key=abc123\nnext", "api-key")]
    [InlineData("tenant_api_key_abc123", "api_key")]
    [InlineData("account_key_abc123", "account_key")]
    public async Task Run_summary_redacts_unsafe_policy_id(string policyId, string marker)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithPolicyId(policyId)
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
        Assert.DoesNotContain(marker, summary.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', summary.Message!);
    }

    [Theory]
    [InlineData("summary-policy", "summary-policy")]
    [InlineData("  summary-policy\r\n", "summary-policy")]
    [InlineData("\u0000summary-policy\u0000", "summary-policy")]
    [InlineData("\u00a0summary-policy\u00a0", "summary-policy")]
    public void SafePolicyId_trims_edge_whitespace_and_controls(string policyId, string expected)
        => Assert.Equal(expected, RunSummaryAuditFields.SafePolicyId(policyId));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("summary policy")]
    [InlineData("summary/policy")]
    [InlineData("summary\\policy")]
    [InlineData("summary\u0000policy")]
    [InlineData("tenant-TOKEN")]
    [InlineData("tenant-client-key-abc123")]
    [InlineData("client_secret")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void SafePolicyId_redacts_unsafe_ids(string policyId)
        => Assert.Equal("[redacted]", RunSummaryAuditFields.SafePolicyId(policyId));

    [Fact]
    public void SafePolicyId_returns_original_safe_id_without_steady_state_allocation()
    {
        const string policyId = "summary-policy";
        for (var i = 0; i < PolicyIdAllocationWarmupIterations; i++)
        {
            GC.KeepAlive(RunSummaryAuditFields.SafePolicyId(policyId));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < PolicyIdAllocationIterations; i++)
        {
            var sanitized = RunSummaryAuditFields.SafePolicyId(policyId);
            if (!ReferenceEquals(policyId, sanitized))
            {
                throw new InvalidOperationException("Safe policy ids should return the original string instance.");
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var allocatedPerCall = (double)allocated / PolicyIdAllocationIterations;

        Console.WriteLine(
            $"SafePolicyId clean allocation: {allocated:N0} B; {allocatedPerCall:N3} B/call.");
        Assert.True(
            allocatedPerCall < 1D,
            $"Expected safe policy-id sanitization to stay near zero allocation; observed {allocatedPerCall:N3} B/call.");
    }

    private static void AssertSummaryContainsExecutionHashes(SandboxAuditEvent summary, ExecutionPlan plan)
    {
        Assert.Contains($"plan={plan.PlanHash}", summary.Message!);
        Assert.Contains($"policy={plan.PolicyHash}", summary.Message!);
        Assert.Contains($"policyId={summary.Fields!["policyId"]}", summary.Message!);
        Assert.Contains($"bindings={plan.BindingManifestHash}", summary.Message!);
    }
}
