using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime;

public sealed class BindingDescriptorLookupCacheTests
{
    [Fact]
    public void Get_binding_descriptor_returns_cached_descriptor_for_same_id()
    {
        var first = Binding("test.first");
        var (context, registry) = Context(first, Binding("test.second"));
        var expected = registry.GetDescriptor(first.Id);

        var resolved = context.GetBindingDescriptor(first.Id);
        var cached = context.GetBindingDescriptor(first.Id);

        Assert.Same(expected, resolved);
        Assert.Same(resolved, cached);
    }

    [Fact]
    public void Get_binding_descriptor_does_not_reuse_cached_descriptor_for_different_id()
    {
        var first = Binding("test.first");
        var second = Binding("test.second");
        var (context, registry) = Context(first, second);
        var expected = registry.GetDescriptor(second.Id);

        _ = context.GetBindingDescriptor(first.Id);
        var resolved = context.GetBindingDescriptor(second.Id);

        Assert.Same(expected, resolved);
    }

    [Fact]
    public void Get_binding_descriptor_preserves_missing_id_failure_after_cache_hit()
    {
        var (context, _) = Context(Binding("test.first"));

        _ = context.GetBindingDescriptor("test.first");

        Assert.Throws<KeyNotFoundException>(() => context.GetBindingDescriptor("test.missing"));
    }

    private static (SandboxContext Context, BindingRegistry Registry) Context(params BindingDescriptor[] bindings)
    {
        var registry = new BindingRegistryBuilder().AddRange(bindings).Build();
        var limits = SandboxPolicyBuilder.Create().Build().ResourceLimits;
        var context = new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build(),
            new ResourceMeter(limits),
            registry,
            new InMemoryAuditSink(),
            CancellationToken.None);
        return (context, registry);
    }

    private static BindingDescriptor Binding(string id)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(
                typeof(global::DotBoxD.Kernels.Runtime.CompiledRuntime).FullName!,
                nameof(global::DotBoxD.Kernels.Runtime.CompiledRuntime.CallBinding)));
}
