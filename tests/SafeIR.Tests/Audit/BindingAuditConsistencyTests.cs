using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class BindingAuditConsistencyTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Success_audit_with_error_code_does_not_satisfy_required_audit(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.WritesSuccessAuditWithErrorCode);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Contains("required audit", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Failure_audit_must_match_actual_error_code(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.WritesWrongFailureAuditAndThrows);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "BindingCall" &&
            e.BindingId == "test.audited" &&
            e.Success == false &&
            e.ErrorCode == SandboxErrorCode.QuotaExceeded);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Deterministic_fallback_failure_audit_uses_policy_logical_clock(ExecutionMode mode)
    {
        var logicalNow = DateTimeOffset.Parse("2026-06-12T12:00:00Z");
        var host = Host(TestBindingBehavior.ThrowsWithoutAudit);
        var module = await host.ImportJsonAsync(ModuleJson());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .Deterministic(logicalNow, randomSeed: 1)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        var audit = Assert.Single(result.AuditEvents, e =>
            e.Kind == "BindingCall" &&
            e.BindingId == "test.audited" &&
            e.Success == false);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, audit.ErrorCode);
        Assert.Equal(logicalNow, audit.Timestamp);
    }

    [Fact]
    public void Capability_denial_audit_includes_resource_identity()
    {
        var audit = new InMemoryAuditSink();
        var policy = SandboxPolicyBuilder.Create().Build();
        var context = new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            audit,
            CancellationToken.None);

        var ex = Assert.Throws<SandboxRuntimeException>(() => context.RequireCapability("test.cap"));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
        Assert.Contains(audit.Events, e =>
            e.Kind == "PolicyDenied" &&
            e.CapabilityId == "test.cap" &&
            e.ResourceId == "capability:test.cap");
    }

    private static SandboxHost Host(TestBindingBehavior behavior)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(AuditedBinding(behavior));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static BindingDescriptor AuditedBinding(TestBindingBehavior behavior)
        => new(
            "test.audited",
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
                if (behavior == TestBindingBehavior.WritesWrongFailureAuditAndThrows)
                {
                    WriteAudit(context, success: false, SandboxErrorCode.NotFound);
                    throw new SandboxRuntimeException(new SandboxError(
                        SandboxErrorCode.QuotaExceeded,
                        "test quota exceeded"));
                }

                if (behavior == TestBindingBehavior.ThrowsWithoutAudit)
                {
                    throw new SandboxRuntimeException(new SandboxError(
                        SandboxErrorCode.QuotaExceeded,
                        "test quota exceeded"));
                }

                WriteAudit(context, success: true, SandboxErrorCode.PermissionDenied);
                return ValueTask.FromResult(SandboxValue.FromInt32(7));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static void WriteAudit(SandboxContext context, bool success, SandboxErrorCode? errorCode)
    {
        var timestamp = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            timestamp,
            success,
            BindingId: "test.audited",
            Effect: SandboxEffect.Cpu,
            ResourceId: "binding:test.audited",
            ErrorCode: errorCode,
            Fields: context.BindingAuditFields("binding", timestamp)));
    }

    private static string ModuleJson()
        => """
        {
          "id": "binding-audit-consistency",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.audited", "args": [] } }]
            }
          ]
        }
        """;

    private enum TestBindingBehavior
    {
        WritesSuccessAuditWithErrorCode,
        WritesWrongFailureAuditAndThrows,
        ThrowsWithoutAudit
    }
}
