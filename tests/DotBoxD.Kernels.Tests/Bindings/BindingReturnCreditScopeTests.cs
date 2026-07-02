using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingReturnCreditScopeTests
{
    [Fact]
    public void Binding_return_credit_scope_dispose_is_idempotent()
    {
        var context = CreateContext();
        var outer = context.BeginBindingReturnCreditScope();
        var inner = context.BeginBindingReturnCreditScope();

        inner.Dispose();
        inner.Dispose();

        context.RecordStringReturnCredit("outer");
        _ = context.ChargeBindingReturn(StringDescriptor(), SandboxValue.FromString("outer"));

        Assert.Equal(0, context.Budget.StringBytes);
        outer.Dispose();
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxTotalStringBytes: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static BindingDescriptor StringDescriptor()
        => new(
            "test.string",
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub("Probe", "Probe"));
}
