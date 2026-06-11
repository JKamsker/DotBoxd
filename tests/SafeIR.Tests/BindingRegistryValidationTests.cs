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

    private static BindingRegistry Build(BindingDescriptor binding)
        => new BindingRegistryBuilder().Add(binding).Build();

    private static BindingDescriptor TestBinding(CompiledBinding compiled)
        => new(
            "test.binding",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            compiled);
}
