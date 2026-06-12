using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class CustomEffectBindingTests
{
    [Fact]
    public async Task Custom_effect_binding_requires_policy_grant()
    {
        var host = HostWithCounterBinding(_ => { });
        var module = await host.ImportJsonAsync(CounterModule());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Custom_effect_binding_executes_with_grant()
    {
        var observed = 0;
        var host = HostWithCounterBinding(value => observed += value);
        var module = await host.ImportJsonAsync(CounterModule());
        var plan = await host.PrepareAsync(
            module,
            GameWritePolicy());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)result.Value!).Value);
        Assert.Equal(7, observed);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Custom_effect_binding_compiled_mode_fails_without_interpreter_fallback()
    {
        var host = HostWithCounterBinding(_ => { });
        var module = await host.ImportJsonAsync(CounterModule());
        var plan = await host.PrepareAsync(
            module,
            GameWritePolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Custom_effect_binding_auto_mode_stays_interpreted()
    {
        var observed = 0;
        var host = HostWithCounterBinding(value => observed += value);
        var module = await host.ImportJsonAsync(CounterModule());
        var plan = await host.PrepareAsync(
            module,
            GameWritePolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(7, observed);
    }

    [Fact]
    public void Registry_rejects_capability_gated_binding_when_effects_are_pure()
    {
        var observed = 0;

        var ex = Assert.Throws<SandboxValidationException>(() => HostWithBinding(CapabilityGatedPureBinding(() => observed++)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-EFFECT");
        Assert.Equal(0, observed);
    }

    private static SandboxHost HostWithCounterBinding(Action<int> record)
        => HostWithBinding(CounterBinding(record));

    private static SandboxHost HostWithBinding(BindingDescriptor binding)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(binding);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy GameWritePolicy()
        => new(
            "game-write",
            SandboxEffect.Cpu | SandboxEffect.GameStateWrite | SandboxEffect.Audit,
            [new CapabilityGrant("game.write", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

    private static BindingDescriptor CounterBinding(Action<int> record)
        => new(
            "app.counter",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.GameStateWrite | SandboxEffect.Audit,
            "game.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, args, _) =>
            {
                var value = ((I32Value)args[0]).Value;
                record(value);
                var timestamp = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    timestamp,
                    true,
                    BindingId: "app.counter",
                    CapabilityId: "game.write",
                    Effect: SandboxEffect.GameStateWrite,
                    ResourceId: "counter:test",
                    Fields: context.BindingAuditFields("counter", timestamp)));
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            NoParameterGrant);

    private static BindingDescriptor CapabilityGatedPureBinding(Action record)
        => new(
            "app.gatedPure",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            "game.write",
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                record();
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

    private static string CounterModule()
        => """
        {
          "id": "custom-effect-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "app.counter", "args": [{ "i32": 7 }] } }]
            }
          ]
        }
        """;

}
