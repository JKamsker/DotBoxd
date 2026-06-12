using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class BindingAuditEnforcementTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Audited_binding_without_binding_audit_fails(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.MissingSuccessAudit);
        var module = await host.ParseJsonAsync(ModuleJson());
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
    public async Task Audited_binding_with_binding_audit_succeeds(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.WritesSuccessAudit);
        var module = await host.ParseJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)result.Value!).Value);
        Assert.Contains(result.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "test.audited");
        Assert.All(result.AuditEvents, e => Assert.True(e.SequenceNumber > 0));
        Assert.Equal(
            result.AuditEvents.Select(e => e.SequenceNumber).Order().ToArray(),
            result.AuditEvents.Select(e => e.SequenceNumber).ToArray());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Audited_binding_failure_without_binding_audit_preserves_error_and_audits_failure(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.ThrowsWithoutAudit);
        var module = await host.ParseJsonAsync(ModuleJson());
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

    [Fact]
    public async Task Debug_trace_does_not_satisfy_required_binding_audit()
    {
        var host = Host(TestBindingBehavior.MissingSuccessAudit);
        var module = await host.ParseJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, EnableDebugTrace = true });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "DebugTrace" && e.BindingId == "test.audited");
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "BindingCall" &&
            e.BindingId == "test.audited" &&
            e.Success == false);
        Assert.DoesNotContain(result.AuditEvents, e =>
            e.Kind != "DebugTrace" &&
            e.BindingId == "test.audited" &&
            e.Success);
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
                if (behavior == TestBindingBehavior.ThrowsWithoutAudit)
                {
                    throw new SandboxRuntimeException(new SandboxError(
                        SandboxErrorCode.QuotaExceeded,
                        "test quota exceeded"));
                }

                if (behavior == TestBindingBehavior.WritesSuccessAudit)
                {
                    context.Audit.Write(new SandboxAuditEvent(
                        context.RunId,
                        "BindingCall",
                        DateTimeOffset.UtcNow,
                        true,
                        BindingId: "test.audited",
                        Effect: SandboxEffect.Cpu,
                        ResourceId: "test:audit"));
                }

                return ValueTask.FromResult(SandboxValue.FromInt32(7));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private enum TestBindingBehavior
    {
        MissingSuccessAudit,
        WritesSuccessAudit,
        ThrowsWithoutAudit
    }

    private static string ModuleJson()
        => """
        {
          "id": "binding-audit-enforcement",
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
}
