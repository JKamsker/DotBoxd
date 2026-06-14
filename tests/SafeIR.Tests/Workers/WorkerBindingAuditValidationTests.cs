using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class WorkerBindingAuditValidationTests
{
    [Fact]
    public async Task Worker_result_with_legitimate_binding_audit_is_accepted()
    {
        var worker = new SandboxHostWorkerClient(() => LoggingHost());
        var host = LoggingHost(builder => builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess));
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

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "SandboxLog");
        Assert.Equal("log.info", audit.BindingId);
        Assert.Equal("log.write", audit.CapabilityId);
        Assert.Equal("log:info", audit.ResourceId);
    }

    private static SandboxHost LoggingHost(Action<SandboxHostBuilder>? configure = null)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            configure?.Invoke(builder);
        });

    private static string LogJson()
        => """
        {
          "id": "worker-audit-validation-log",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "worker ok" }] } }
              ]
            }
          ]
        }
        """;
}
