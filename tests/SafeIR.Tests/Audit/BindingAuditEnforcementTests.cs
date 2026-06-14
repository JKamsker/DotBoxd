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
    public async Task Audited_binding_with_binding_audit_succeeds(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.WritesSuccessAudit);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, Policy(TestBindingBehavior.WritesSuccessAudit));

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
    [InlineData(TestBindingBehavior.WrongAuditKind)]
    [InlineData(TestBindingBehavior.WrongCapability)]
    [InlineData(TestBindingBehavior.EmptyAuditEffect)]
    [InlineData(TestBindingBehavior.EmptyAuditResource)]
    [InlineData(TestBindingBehavior.EmptyAuditFields)]
    [InlineData(TestBindingBehavior.MissingAuditCorrelation)]
    public async Task Malformed_binding_audit_does_not_satisfy_required_audit(TestBindingBehavior behavior)
    {
        var host = Host(behavior);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, Policy(behavior));

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Contains("required audit", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Audited_binding_return_quota_is_reported_before_missing_success_audit(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.ReturnsLargeStringWithoutAudit);
        var module = await host.ImportJsonAsync(ModuleJson(returnType: "String"));
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).WithMaxTotalStringBytes(4).Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

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
    public async Task Audited_binding_failure_without_binding_audit_preserves_error_and_audits_failure(ExecutionMode mode)
    {
        var host = Host(TestBindingBehavior.ThrowsWithoutAudit);
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

    [Fact]
    public async Task Debug_trace_does_not_satisfy_required_binding_audit()
    {
        var host = Host(TestBindingBehavior.MissingSuccessAudit);
        var module = await host.ImportJsonAsync(ModuleJson());
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

    private static SandboxPolicy Policy(TestBindingBehavior behavior)
    {
        var builder = SandboxPolicyBuilder.Create().WithFuel(1_000);
        return behavior == TestBindingBehavior.WrongCapability
            ? builder.Grant("test.cap", new { }, SandboxEffect.Audit).Build()
            : builder.Build();
    }

    private static BindingDescriptor AuditedBinding(TestBindingBehavior behavior)
        => new(
            "test.audited",
            SemVersion.One,
            [],
            behavior == TestBindingBehavior.ReturnsLargeStringWithoutAudit ? SandboxType.String : SandboxType.I32,
            behavior == TestBindingBehavior.WrongCapability ? SandboxEffect.Cpu | SandboxEffect.Audit : SandboxEffect.Cpu,
            behavior == TestBindingBehavior.WrongCapability ? "test.cap" : null,
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
                    WriteAudit(context, "BindingCall", "test.cap", SandboxEffect.Cpu, "test:audit");
                }

                if (behavior == TestBindingBehavior.WrongAuditKind)
                {
                    WriteAudit(context, "RunSummary", null, SandboxEffect.Cpu, "test:audit");
                }

                if (behavior == TestBindingBehavior.WrongCapability)
                {
                    WriteAudit(context, "BindingCall", "other.cap", SandboxEffect.Cpu, "test:audit");
                }

                if (behavior == TestBindingBehavior.EmptyAuditEffect)
                {
                    WriteAudit(context, "BindingCall", null, SandboxEffect.None, "test:audit");
                }

                if (behavior == TestBindingBehavior.EmptyAuditResource)
                {
                    WriteAudit(context, "BindingCall", null, SandboxEffect.Cpu, null);
                }

                if (behavior == TestBindingBehavior.EmptyAuditFields)
                {
                    WriteAudit(context, "BindingCall", null, SandboxEffect.Cpu, "test:audit", includeFields: false);
                }

                if (behavior == TestBindingBehavior.MissingAuditCorrelation)
                {
                    WriteAudit(context, "BindingCall", null, SandboxEffect.Cpu, "test:audit", includeCorrelation: false);
                }

                if (behavior == TestBindingBehavior.ReturnsLargeStringWithoutAudit)
                {
                    return ValueTask.FromResult(SandboxValue.FromString("oversized"));
                }

                return ValueTask.FromResult(SandboxValue.FromInt32(7));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            NoParameterGrant);

    private static void NoParameterGrant(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter '{key}' is not supported"));
        }
    }

    private static void WriteAudit(
        SandboxContext context,
        string kind,
        string? capabilityId,
        SandboxEffect effect,
        string? resourceId,
        bool includeFields = true,
        bool includeCorrelation = true)
    {
        var timestamp = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            kind,
            timestamp,
            true,
            BindingId: "test.audited",
            CapabilityId: capabilityId,
            Effect: effect,
            ResourceId: resourceId,
            Fields: includeFields
                ? includeCorrelation
                    ? context.BindingAuditFields("test", timestamp)
                    : BindingAuditFields.Create("test", timestamp)
                : null));
    }

    public enum TestBindingBehavior
    {
        MissingSuccessAudit,
        WritesSuccessAudit,
        ThrowsWithoutAudit,
        WrongAuditKind,
        WrongCapability,
        EmptyAuditEffect,
        EmptyAuditResource,
        EmptyAuditFields,
        MissingAuditCorrelation,
        ReturnsLargeStringWithoutAudit
    }

    private static string ModuleJson(string returnType = "I32")
        => """
        {
          "id": "binding-audit-enforcement",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{returnType}",
              "body": [{ "op": "return", "value": { "call": "test.audited", "args": [] } }]
            }
          ]
        }
        """.Replace("{returnType}", returnType, StringComparison.Ordinal);
}
