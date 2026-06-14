using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class Fix_PAL_0023_Tests
{
    [Fact]
    public void Contains_returns_true_for_existing_binding()
    {
        var registry = CreateRegistry();

        Assert.True(registry.Contains("test.pure"));
    }

    [Fact]
    public void Contains_returns_false_for_unknown_binding()
    {
        var registry = CreateRegistry();

        Assert.False(registry.Contains("test.missing"));
    }

    [Fact]
    public void Contains_agrees_with_TryGet_existence_for_present_and_absent_ids()
    {
        var registry = CreateRegistry();

        Assert.Equal(registry.TryGet("test.pure", out _), registry.Contains("test.pure"));
        Assert.Equal(registry.TryGet("test.missing", out _), registry.Contains("test.missing"));
    }

    [Fact]
    public void Contains_is_exposed_through_the_catalog_interface()
    {
        IBindingCatalog catalog = CreateRegistry();

        Assert.True(catalog.Contains("test.pure"));
        Assert.False(catalog.Contains("test.missing"));
    }

    [Fact]
    public void Contains_does_not_materialize_a_signature_copy()
    {
        // Existence checks must not allocate a fresh BindingSignature/parameter
        // array the way TryGet does. Each TryGet on a many-parameter binding
        // builds a new signature with a copied parameter array; Contains must not.
        var registry = CreateRegistry();

        registry.Contains("test.many"); // warm up any JIT/first-call allocations

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            _ = registry.Contains("test.many");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static BindingRegistry CreateRegistry()
        => new BindingRegistryBuilder()
            .Add(PureBinding("test.pure", parameterCount: 0))
            .Add(PureBinding("test.many", parameterCount: 4))
            .Build();

    private static BindingDescriptor PureBinding(string id, int parameterCount)
    {
        var parameters = new SandboxType[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            parameters[i] = SandboxType.I32;
        }

        return new BindingDescriptor(
            id,
            SemVersion.One,
            parameters,
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(0)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
    }
}
