using DotBoxD.Hosting;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerFallbackAuditTests
{
    [Fact]
    public async Task Worker_process_accepts_compiled_to_interpreted_fallback_audit()
    {
        var worker = new SandboxHostWorkerClient(WorkerHost);
        using var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                Isolation = SandboxIsolation.WorkerProcess
            });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(result.ExecutionDispatched);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, e => e.Kind == "ExecutionFallback");
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost WorkerHost()
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
}
