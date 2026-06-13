namespace SafeIR.Example.Capabilities;

using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;
using SafeIR.Serialization.Json;

/// <summary>
/// Package-backed walkthrough for authoring a custom host binding.
///
/// A host owns a <see cref="BindingDescriptor"/>, registers it with
/// <see cref="SandboxHostBuilder.AddBinding"/>, grants the matching capability through
/// <see cref="SandboxPolicyBuilder"/>, imports JSON Safe IR that calls the binding, and
/// then inspects the returned value plus the binding audit fields. This is the minimal
/// end-to-end shape a binding author needs: required capability, deterministic cost model,
/// audit level, <see cref="BindingSafety"/>, the grant validator, and the compiled runtime
/// stub used for compiled mode.
/// </summary>
internal static class CustomBindingExample
{
    // The host-defined capability gating the binding. Bindings with external/side effects
    // must require a capability so policy stays the trust boundary.
    private const string TenantReadCapability = "tenant.read";
    private const string TenantLookupBindingId = "tenant.lookup";

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(TenantLookupBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

        var module = await host.ImportJsonAsync(TenantLookupModule());
        var plan = await host.PrepareAsync(module, TenantReadPolicy(maxTenantId: 100));

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        var value = ((I32Value)result.Value!).Value;
        var auditEvent = result.AuditEvents.Single(e => e.BindingId == TenantLookupBindingId);

        Console.WriteLine(
            $"custom binding: value={value}, hostCalls={result.ResourceUsage.HostCalls}, " +
            $"capability={auditEvent.CapabilityId}, resource={auditEvent.ResourceId}, " +
            $"resourceKind={auditEvent.Fields?["resourceKind"]}");
    }

    // The host policy grants the capability and the matching effects. The grant carries a
    // host-owned parameter (maxTenantId) that the descriptor's grant validator can enforce.
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
            // Read-only external access still emits audit, so include the Audit effect.
            SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            TenantReadCapability,
            // Deterministic, fixed cost: one host call charged a flat fuel amount.
            BindingCostModel.Fixed(8),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var tenantId = ((I32Value)args[0]).Value;

                // A real binding would call host-owned tenant storage here. The example resolves a
                // deterministic value so the smoke output is stable.
                var resolved = tenantId * 10;

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

                return ValueTask.FromResult(SandboxValue.FromInt32(resolved));
            },
            // Compiled mode dispatches custom bindings through the shared runtime stub.
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            ValidateTenantReadGrant);

    // The grant validator runs during preparation and fails closed on unsupported or invalid
    // grant parameters. Here only the optional "maxTenantId" bound is accepted.
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

        if (grant.Parameters.TryGetValue("maxTenantId", out var raw) &&
            (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var max) || max <= 0))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter 'maxTenantId' must be a positive integer"));
        }
    }

    private static string TenantLookupModule()
        => """
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
}
