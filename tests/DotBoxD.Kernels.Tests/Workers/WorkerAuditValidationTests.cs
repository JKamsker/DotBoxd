using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using static DotBoxD.Kernels.Tests.Workers.WorkerAuditValidationTestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerAuditValidationTests
{
    [Fact]
    public async Task Worker_result_with_undefined_non_summary_error_code_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "WorkerExecution",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: (SandboxErrorCode)123456));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_forged_binding_audit_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "BindingCall",
            DateTimeOffset.UtcNow,
            true,
            BindingId: "math.abs",
            Effect: SandboxEffect.Cpu,
            ResourceId: "binding:math.abs",
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceKind"] = "binding",
                ["durationMs"] = "0",
                ["moduleHash"] = plan.ModuleHash,
                ["policyHash"] = plan.PolicyHash
            }));
        var host = Host(worker);
        var plan = await PrepareAsync(host, MathBindingModule());

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.BindingId == "math.abs");
    }

    [Fact]
    public async Task Worker_result_with_unknown_non_summary_audit_kind_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "ForgedEvidence",
            DateTimeOffset.UtcNow,
            true,
            ResourceId: $"module:{plan.ModuleHash}"));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_forged_policy_denied_audit_is_rejected()
    {
        var worker = new AuditForgingWorker((_, runId) => new SandboxAuditEvent(
            runId,
            "PolicyDenied",
            DateTimeOffset.UtcNow,
            false,
            CapabilityId: "file.write",
            ResourceId: "capability:file.write",
            ErrorCode: SandboxErrorCode.PolicyDenied,
            Fields: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resourceKind"] = "capability",
                ["forged"] = "field"
            }));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "PolicyDenied");
    }

    [Fact]
    public async Task Worker_result_with_forged_cache_invalidated_audit_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "CacheInvalidated",
            DateTimeOffset.UtcNow,
            true,
            ResourceId: $"module:{plan.ModuleHash}"));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "CacheInvalidated");
    }

    [Fact]
    public async Task Worker_result_with_forged_audit_timestamp_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "WorkerExecution",
            DateTimeOffset.UtcNow.AddYears(10),
            true,
            ResourceId: $"module:{plan.ModuleHash}"));
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_extra_run_summary_field_is_rejected()
    {
        var worker = new AuditForgingWorker(
            (plan, runId) => new SandboxAuditEvent(
                runId,
                "WorkerExecution",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}"),
            AddSummaryExtraField: true);
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_result_with_unredacted_log_audit_text_is_rejected()
    {
        var worker = new AuditForgingWorker((plan, runId) => new SandboxAuditEvent(
            runId,
            "SandboxLog",
            DateTimeOffset.UtcNow,
            true,
            BindingId: "log.info",
            CapabilityId: "log.write",
            Effect: SandboxEffect.Audit,
            ResourceId: "log:info",
            Message: "token=abc123 password=hunter2",
            Fields: BindingFields(plan, "log")),
            Value: SandboxValue.Unit);
        var host = LogHost(worker);
        var module = await host.ImportJsonAsync(LogJson());
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(1_000)
            .WithMaxLogEvents(1)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.DoesNotContain("abc123", string.Join('\n', result.AuditEvents.Select(e => e.Message)));
    }

    [Fact]
    public async Task Worker_result_with_forged_run_summary_policy_id_is_rejected()
    {
        var worker = new AuditForgingWorker(
            (plan, runId) => new SandboxAuditEvent(
                runId,
                "WorkerExecution",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}"),
            MutateSummaryFields: fields => fields["policyId"] = "tenant_api_key_abc123");
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_path_clears_successful_summary_suppression_so_audit_stays_valid()
    {
        // SuppressSuccessfulRunSummaryAudit is an in-process-only optimization. Worker-result
        // validation requires exactly one RunSummary, so the executor must strip the flag before
        // handing options to the worker; otherwise a suppressed successful run would emit no
        // summary and fail validation. The worker here mirrors the real runner: it suppresses the
        // summary only if the flag survives.
        SandboxExecutionOptions? observed = null;
        var worker = new RecordingWorker(options => observed = options);
        var host = Host(worker);
        var plan = await PrepareAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Isolation = SandboxIsolation.WorkerProcess,
                SuppressSuccessfulRunSummaryAudit = true
            });

        Assert.NotNull(observed);
        Assert.False(observed!.SuppressSuccessfulRunSummaryAudit);
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
    }

}
