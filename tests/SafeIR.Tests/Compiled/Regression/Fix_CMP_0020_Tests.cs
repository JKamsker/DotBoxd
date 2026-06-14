using SafeIR.Hosting;

namespace SafeIR.Tests;

// Regression coverage for CMP-0020: SafeIR ships sandbox-visible logging bindings and log
// quotas, but there was no standalone runtime proof that a host registers log.info/log.warn,
// grants log.write, applies a log quota, and inspects the sanitized audit + ResourceUsage.LogEvents
// output. The runnable walkthrough now lives in
// examples/Capabilities/SafeIR.Example.Capabilities/Examples/SafeLoggingExample.cs (exercised by the docs
// smoke). These tests lock the public capability boundary that walkthrough demonstrates so the
// example cannot silently drift from the bindings/policy contract it documents.
public sealed class Fix_CMP_0020_Tests
{
    private const string WalkthroughModuleJson = """
    {
      "id": "logging-walkthrough",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "log.write", "reason": "Emit operational logs" }],
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "Unit",
          "body": [
            { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "starting run token=abc123" }] } },
            { "op": "return", "value": { "call": "log.warn", "args": [{ "string": "low fuel" }] } }
          ]
        }
      ]
    }
    """;

    [Fact]
    public async Task Walkthrough_grants_logging_and_exposes_sanitized_audit_and_log_event_count()
    {
        var host = CreateLoggingHost();
        var module = await host.ImportJsonAsync(WalkthroughModuleJson);
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithMaxLogEvents(8)
            .WithMaxLogMessageLength(256)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(2, result.ResourceUsage.LogEvents);

        var info = Assert.Single(result.AuditEvents, e => e.Kind == "SandboxLog" && e.BindingId == "log.info");
        Assert.Equal("log.write", info.CapabilityId);
        Assert.Equal("log:info", info.ResourceId);
        Assert.DoesNotContain("abc123", info.Message, StringComparison.Ordinal);

        var warn = Assert.Single(result.AuditEvents, e => e.Kind == "SandboxLog" && e.BindingId == "log.warn");
        Assert.Equal("low fuel", warn.Message);
    }

    [Fact]
    public async Task Walkthrough_requires_the_logging_grant_in_addition_to_the_bindings()
    {
        var host = CreateLoggingHost();
        var module = await host.ImportJsonAsync(WalkthroughModuleJson);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Walkthrough_quota_returns_documented_quota_exceeded_shape()
    {
        var host = CreateLoggingHost();
        var module = await host.ImportJsonAsync(WalkthroughModuleJson);
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithMaxLogEvents(1)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    private static SandboxHost CreateLoggingHost()
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
        });
}
