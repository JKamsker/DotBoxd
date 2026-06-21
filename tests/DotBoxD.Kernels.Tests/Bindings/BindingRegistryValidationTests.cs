using DotBoxD.Hosting.Http.Bindings;
using DotBoxD.Hosting.Http.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingRegistryValidationTests
{
    [Fact]
    public void Default_pure_bindings_contain_math_intrinsics()
    {
        var registry = new BindingRegistryBuilder()
            .AddDefaultPureBindings()
            .Build();

        Assert.True(registry.TryGet("math.abs", out _));
        Assert.True(registry.TryGet("math.sqrt", out _));
        Assert.False(registry.TryGet("app.nope", out _));
    }

    [Fact]
    public void Binding_registry_rejects_duplicate_binding_id()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder()
                .Add(TestBinding(CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding))))
                .Add(TestBinding(CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding))))
                .Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-DUP");
    }

    [Fact]
    public void Binding_registry_rejects_empty_binding_id()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
                id: "")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-ID");
    }

    [Fact]
    public void Binding_registry_rejects_clr_looking_binding_id()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
                id: "System.IO.File.ReadAllText")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-ID");
    }

    [Fact]
    public void Binding_registry_rejects_clr_looking_required_capability()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
                effects: SandboxEffect.FileRead,
                requiredCapability: "System.IO.File")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-CAP");
    }

    [Fact]
    public void Binding_registry_rejects_unsupported_compiled_target_kind()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(new CompiledBinding("DirectMethod", typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)))));

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
            TestBinding(CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.SafeFileSystem", "ReadTextAsync"))));

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
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.AbsI32)),
                BindingSafety.PureHostFacade)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_audited_direct_runtime_method()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.AbsI32)),
                BindingSafety.PureIntrinsic,
                auditLevel: AuditLevel.PerCall)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_direct_runtime_method_with_wrong_signature()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.AbsI32)),
                BindingSafety.PureIntrinsic)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void Binding_registry_rejects_side_effecting_binding_without_capability()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
            effects: SandboxEffect.FileRead)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-CAP");
    }

    [Fact]
    public void Binding_registry_rejects_dangerous_binding()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
            safety: BindingSafety.DangerousRequiresReview)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-DANGER");
    }

    [Theory]
    [MemberData(nameof(NegativeCostModels))]
    public void Binding_registry_rejects_negative_cost_model(BindingCostModel costModel)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(
            TestBinding(
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
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
        Assert.All(registry.Signatures, binding =>
        {
            Assert.Equal("RuntimeStub", binding.Compiled.Kind);
            Assert.Equal(typeof(CompiledRuntime).FullName, binding.Compiled.Type);
        });
    }

    [Fact]
    public void Network_binding_api_only_accepts_in_memory_invokers()
    {
        Assert.DoesNotContain(
            typeof(SandboxHostBuilder).GetMethods(),
            method => method.Name == nameof(SafeHttpHostBuilderExtensions.AddNetworkBindings));

        var method = typeof(SafeHttpHostBuilderExtensions).GetMethods()
            .Single(m => m.Name == nameof(SafeHttpHostBuilderExtensions.AddNetworkBindings));
        var parameters = method.GetParameters();

        Assert.Equal(typeof(SandboxHostBuilder), parameters[0].ParameterType);
        Assert.Equal(typeof(SafeInMemoryHttpMessageInvoker), parameters[1].ParameterType);
        Assert.Equal(typeof(SafeDnsResolver), parameters[2].ParameterType);
    }

    private static BindingRegistry Build(BindingDescriptor binding)
        => new BindingRegistryBuilder().Add(binding).Build();

    private static BindingDescriptor TestBinding(
        CompiledBinding compiled,
        BindingSafety safety = BindingSafety.PureHostFacade,
        BindingCostModel? costModel = null,
        SandboxEffect effects = SandboxEffect.Cpu,
        string id = "test.binding",
        string? requiredCapability = null,
        AuditLevel auditLevel = AuditLevel.None)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.Unit,
            effects,
            requiredCapability,
            costModel ?? BindingCostModel.Fixed(1),
            auditLevel,
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
