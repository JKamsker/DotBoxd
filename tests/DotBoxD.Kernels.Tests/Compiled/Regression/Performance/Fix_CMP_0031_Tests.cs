using DotBoxD.Hosting;

namespace DotBoxD.Kernels.Tests;

public sealed class Fix_CMP_0031_Tests
{
    [Fact]
    public async Task Suppressed_successful_no_binding_compiled_run_emits_no_audit_events()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var plan = await PreparePurePlanAsync(host, BuildPolicy());

        var result = await ExecuteCompiledAsync(
            host,
            plan,
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            suppressSuccessfulSummary: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Empty(result.AuditEvents);
        Assert.Empty(observed);
    }

    [Fact]
    public async Task Suppressed_no_binding_compiled_failure_still_emits_failed_run_summary()
    {
        using var host = BuildCompiledHost();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .WithMaxTotalStringBytes(1)
            .Build();
        var plan = await PreparePurePlanAsync(host, policy);

        var result = await ExecuteCompiledAsync(
            host,
            plan,
            SandboxValue.FromString("too long"),
            suppressSuccessfulSummary: true);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, summary.ErrorCode);
    }

    private static SandboxHost BuildCompiledHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static async Task<ExecutionPlan> PreparePurePlanAsync(SandboxHost host, SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, policy);
    }

    private static SandboxPolicy BuildPolicy()
        => SandboxPolicyBuilder.Create().WithFuel(10_000).Build();

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        bool suppressSuccessfulSummary)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = suppressSuccessfulSummary
            });

}
