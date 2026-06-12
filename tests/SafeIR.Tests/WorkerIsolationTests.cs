namespace SafeIR.Tests;

public sealed class WorkerIsolationTests
{
    [Fact]
    public async Task Worker_process_isolation_request_fails_closed_when_unconfigured()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Auto,
                Isolation = SandboxIsolation.WorkerProcess
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Null(result.ArtifactHash);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationUnavailable");
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.PolicyDenied, summary.ErrorCode);
        Assert.Equal("Auto", summary.Fields!["mode"]);
    }
}
