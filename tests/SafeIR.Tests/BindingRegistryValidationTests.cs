using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class BindingRegistryValidationTests
{
    [Fact]
    public void Binding_registry_rejects_unsupported_compiled_target_kind()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(new CompiledBinding("DirectMethod", typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_incomplete_compiled_target()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(new CompiledBinding("RuntimeStub", typeof(CompiledRuntime).FullName!, ""))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_compiled_target_outside_runtime_surface()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(CompiledBinding.RuntimeStub("System.IO.File", "ReadAllText"))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_runtime_type_outside_compiled_runtime()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(CompiledBinding.RuntimeStub("SafeIR.Runtime.SafeFileSystem", "ReadTextAsync"))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_unknown_compiled_runtime_method()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, "DeleteEverything"))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_direct_runtime_method_for_host_facade()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.AbsI32)),
                BindingSafety.PureHostFacade)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Theory]
    [MemberData(nameof(NegativeCostModels))]
    public void Binding_registry_rejects_negative_cost_model(BindingCostModel costModel)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
                costModel: costModel)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COST");
    }

    [Fact]
    public void Default_bindings_use_approved_compiled_targets()
    {
        var registry = new BindingRegistryBuilder()
            .AddDefaultPureBindings()
            .AddFileBindings()
            .AddTimeBindings()
            .AddRandomBindings()
            .AddNetworkBindings()
            .AddLogBindings()
            .Build();

        Assert.NotEmpty(registry.Signatures);
        Assert.All(registry.Signatures, binding => {
            Assert.Equal("RuntimeStub", binding.Compiled.Kind);
            Assert.Equal(typeof(CompiledRuntime).FullName, binding.Compiled.Type);
        });
    }

    private static BindingRegistry Build(BindingDescriptor binding)
        => new BindingRegistryBuilder().Add(binding).Build();

    private static BindingDescriptor TestBinding(
        CompiledBinding compiled,
        BindingSafety safety = BindingSafety.PureHostFacade,
        BindingCostModel? costModel = null)
        => new(
            "test.binding",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            costModel ?? BindingCostModel.Fixed(1),
            AuditLevel.None,
            safety,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            compiled);

    public static TheoryData<BindingCostModel> NegativeCostModels()
        => new() {
            new BindingCostModel(BaseFuel: -1),
            new BindingCostModel(BaseFuel: 1, PerByteFuel: -1),
            new BindingCostModel(BaseFuel: 1, MaxCallsPerRun: -1)
        };
}
