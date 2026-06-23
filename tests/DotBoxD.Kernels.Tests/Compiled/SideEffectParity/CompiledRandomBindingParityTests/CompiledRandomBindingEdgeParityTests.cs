using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledRandomBindingEdgeParityTests
{
    [Fact]
    public async Task RandomNextI32_prepare_without_capability_grant_throws_validation_exception()
    {
        var host = RandomParityTestSupport.CreateHost();
        var module = await host.ImportJsonAsync(
            RandomParityTestSupport.SingleCallModuleJson("rand-parity-no-cap"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(10_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }

    [Fact]
    public async Task RandomNextI32_compiled_non_deterministic_value_is_within_range_and_audit_emitted()
    {
        var host = RandomParityTestSupport.CreateHost();
        var module = await host.ImportJsonAsync(
            RandomParityTestSupport.SingleCallModuleJson("rand-parity-nondeterministic"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantRandom()
                .WithFuel(10_000)
                .Build());

        var result = await RandomParityTestSupport.ExecuteAsync(host, plan, ExecutionMode.Compiled);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);

        var value = ((I32Value)result.Value!).Value;
        Assert.InRange(value, 0, 99);

        var audit = Assert.Single(
            result.AuditEvents,
            e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32");
        Assert.True(audit.Success);
        Assert.Equal("random", audit.CapabilityId);
        Assert.Equal(SandboxEffect.Random, audit.Effect);
        Assert.Equal("random:i32", audit.ResourceId);
    }
}
