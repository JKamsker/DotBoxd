using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class HostCallLimitTests
{
    [Fact]
    public async Task Global_host_call_limit_is_enforced()
    {
        var host = CreateHost(TestBinding("test.ping"));
        var module = await host.ImportJsonAsync(DoubleCallJson("test.ping"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxHostCalls(1).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(2, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Per_binding_call_limit_is_enforced()
    {
        var host = CreateHost(TestBinding("test.ping", maxCallsPerRun: 1));
        var module = await host.ImportJsonAsync(DoubleCallJson("test.ping"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxHostCalls(10).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(2, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public void Compiled_runtime_binding_stub_uses_same_call_limits()
    {
        var binding = TestBinding("test.ping", maxCallsPerRun: 1);
        var registry = new BindingRegistryBuilder().Add(binding).Build();
        var policy = SandboxPolicyBuilder.Create().WithMaxHostCalls(10).Build();
        var context = new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            registry,
            new InMemoryAuditSink(),
            CancellationToken.None);

        _ = CompiledRuntime.CallBinding(context, "test.ping", []);
        var ex = Assert.Throws<SandboxRuntimeException>(() => CompiledRuntime.CallBinding(context, "test.ping", []));

        Assert.Equal(SandboxErrorCode.QuotaExceeded, ex.Error.Code);
        Assert.Equal(2, context.Budget.HostCalls);
    }

    [Fact]
    public void Host_call_limit_is_part_of_policy_hash()
    {
        var first = SandboxPolicyBuilder.Create().WithMaxHostCalls(1).Build();
        var second = SandboxPolicyBuilder.Create().WithMaxHostCalls(2).Build();

        Assert.NotEqual(first.Hash, second.Hash);
    }

    private static SandboxHost CreateHost(BindingDescriptor binding)
        => SandboxHost.Create(builder => {
            builder.AddBinding(binding);
            builder.UseInterpreter();
        });

    private static BindingDescriptor TestBinding(string id, int? maxCallsPerRun = null)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            new BindingCostModel(1, MaxCallsPerRun: maxCallsPerRun),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string DoubleCallJson(string bindingId)
        => $$"""
        {
          "id": "host-call-limit",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "{{bindingId}}", "args": [] } },
                { "op": "return", "value": { "call": "{{bindingId}}", "args": [] } }
              ]
            }
          ]
        }
        """;
}
