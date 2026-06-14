using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

// Regression coverage for CMP-0022: DotBoxd.Kernels exposes custom host binding authoring as public API
// (SandboxHostBuilder.AddBinding + BindingDescriptor + CapabilityGrantValidator), but there was no
// package-backed public example or docs smoke that registers a custom binding, grants the matching
// capability, executes JSON IR against it, and inspects the expected audit/resource result. The
// runnable walkthrough now lives in
// samples/Kernels/Capabilities/DotBoxd.Kernels.Example.Capabilities/Examples/CustomBindingExample.cs (exercised by the docs
// smoke). These tests lock the public custom-binding boundary that walkthrough demonstrates so the
// example cannot silently drift from the descriptor/policy/grant-validator contract it documents.
public sealed class Fix_CMP_0022_Tests
{
    private const string TenantReadCapability = "tenant.read";
    private const string TenantLookupBindingId = "tenant.lookup";

    private const string WalkthroughModuleJson = """
    {
      "id": "custom-binding-example",
      "version": "1.0.0",
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "call": "tenant.lookup", "args": [{ "i32": 7 }] } }]
        }
      ]
    }
    """;

    [Fact]
    public async Task Walkthrough_registers_custom_binding_and_exposes_value_audit_and_host_calls()
    {
        var host = CreateTenantHost();
        var module = await host.ImportJsonAsync(WalkthroughModuleJson);
        var plan = await host.PrepareAsync(module, TenantReadPolicy(maxTenantId: 100));

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(70, ((I32Value)result.Value!).Value);
        Assert.Equal(1, result.ResourceUsage.HostCalls);

        var audit = Assert.Single(
            result.AuditEvents,
            e => e.Kind == "BindingCall" && e.BindingId == TenantLookupBindingId);
        Assert.Equal(TenantReadCapability, audit.CapabilityId);
        Assert.Equal(SandboxEffect.HostStateRead, audit.Effect);
        Assert.Equal("tenant:7", audit.ResourceId);
        Assert.Equal("tenant", audit.Fields?["resourceKind"]);
    }

    [Fact]
    public async Task Walkthrough_requires_the_capability_grant_in_addition_to_the_binding()
    {
        var host = CreateTenantHost();
        var module = await host.ImportJsonAsync(WalkthroughModuleJson);

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Walkthrough_grant_validator_rejects_unsupported_grant_parameters()
    {
        var host = CreateTenantHost();
        var module = await host.ImportJsonAsync(WalkthroughModuleJson);
        var policy = SandboxPolicyBuilder.Create()
            .Grant(
                TenantReadCapability,
                new { region = "eu" },
                SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)
            .WithFuel(10_000)
            .WithMaxHostCalls(16)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    private static SandboxHost CreateTenantHost()
        => SandboxHost.Create(builder => {
            builder.AddBinding(TenantLookupBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy TenantReadPolicy(int maxTenantId)
        => SandboxPolicyBuilder.Create()
            .Grant(
                TenantReadCapability,
                new { maxTenantId },
                SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)
            .WithFuel(10_000)
            .WithMaxHostCalls(16)
            .Build();

    private static BindingDescriptor TenantLookupBinding()
        => new(
            TenantLookupBindingId,
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            TenantReadCapability,
            BindingCostModel.Fixed(8),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var tenantId = ((I32Value)args[0]).Value;
                var startedAt = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    Success: true,
                    BindingId: TenantLookupBindingId,
                    CapabilityId: TenantReadCapability,
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"tenant:{tenantId}",
                    Fields: context.BindingAuditFields("tenant", startedAt)));

                return ValueTask.FromResult(SandboxValue.FromInt32(tenantId * 10));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            ValidateTenantReadGrant);

    private static void ValidateTenantReadGrant(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            if (key != "maxTenantId")
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT-PARAM",
                    $"grant '{grant.Id}' parameter '{key}' is not supported"));
            }
        }
    }
}
