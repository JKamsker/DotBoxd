using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

public sealed class BindingAuditCorrelationTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("module")]
    [InlineData("policy")]
    public async Task Binding_audit_must_match_current_run_and_plan_hashes(string mismatch)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(AuditedBinding(mismatch));
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Contains("required audit", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    private static BindingDescriptor AuditedBinding(string mismatch)
        => new(
            "test.correlatedAudit",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.PureHostFacade,
            (context, _, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var runId = mismatch == "run" ? SandboxRunId.New() : context.RunId;
                var moduleHash = mismatch == "module" ? "wrong-module" : context.ModuleHash;
                var policyHash = mismatch == "policy" ? "wrong-policy" : context.PolicyHash;
                context.Audit.Write(new SandboxAuditEvent(
                    runId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "test.correlatedAudit",
                    Effect: SandboxEffect.Cpu,
                    ResourceId: "binding:test.correlatedAudit",
                    Fields: BindingAuditFields.Create(
                        "binding",
                        startedAt,
                        moduleHash,
                        policyHash,
                        context.Policy.Deterministic)));
                return ValueTask.FromResult(SandboxValue.FromInt32(1));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string ModuleJson()
        => """
        {
          "id": "binding-audit-correlation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.correlatedAudit", "args": [] } }]
            }
          ]
        }
        """;
}
