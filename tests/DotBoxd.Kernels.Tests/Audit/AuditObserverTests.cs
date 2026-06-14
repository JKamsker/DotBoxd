using DotBoxd.Kernels;
using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

public sealed class AuditObserverTests
{
    [Fact]
    public async Task Host_forwards_run_audit_events_to_operational_observer()
    {
        var observed = new List<SandboxAuditEvent>();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(result.AuditEvents, observed);
        Assert.Contains(observed, e => e.Kind == "RunSummary");
    }

    [Fact]
    public async Task Host_forwards_fail_closed_audit_events_to_operational_observer()
    {
        var observed = new List<SandboxAuditEvent>();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(result.AuditEvents, observed);
        Assert.Contains(observed, e => e.Kind == "WorkerIsolationUnavailable");
    }

    [Fact]
    public async Task Throwing_audit_observer_does_not_change_successful_execution_result()
    {
        var observed = new List<SandboxAuditEvent>();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(_ => throw new InvalidOperationException("observer failed"));
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(result.AuditEvents, observed);
        Assert.Contains(observed, e => e.Kind == "RunSummary");
    }

    [Fact]
    public async Task Throwing_audit_observer_does_not_change_fail_closed_execution_result()
    {
        var observed = new List<SandboxAuditEvent>();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.ForwardAuditEventsTo(_ => throw new InvalidOperationException("observer failed"));
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(result.AuditEvents, observed);
        Assert.Contains(observed, e => e.Kind == "WorkerIsolationUnavailable");
    }
}
