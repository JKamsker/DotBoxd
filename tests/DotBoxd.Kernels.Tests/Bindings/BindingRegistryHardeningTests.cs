using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

public sealed class BindingRegistryHardeningTests
{
    [Fact]
    public void Binding_registry_rejects_binding_without_effects()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(effects: SandboxEffect.None)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-EFFECT");
    }

    [Fact]
    public void Binding_registry_rejects_external_binding_without_audit()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.HostStateRead,
            requiredCapability: "game.read",
            safety: BindingSafety.ReadOnlyExternal,
            grantValidator: NoParameters)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void Binding_registry_rejects_effectful_binding_without_audit_even_when_safety_is_mislabeled()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.FileRead,
            requiredCapability: "file.read",
            safety: BindingSafety.PureHostFacade)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void Binding_registry_constructor_validates_descriptors()
    {
        var descriptors = new[]
        {
            TestBinding(
                effects: SandboxEffect.FileRead,
                requiredCapability: "file.read",
                safety: BindingSafety.PureHostFacade)
        };

        var ex = Assert.Throws<SandboxValidationException>(() => new BindingRegistry(descriptors));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void Binding_registry_rejects_custom_capability_without_validator()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.HostStateRead | SandboxEffect.Audit,
            requiredCapability: "game.read",
            safety: BindingSafety.ReadOnlyExternal,
            auditLevel: AuditLevel.PerCall)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-GRANT");
    }

    [Fact]
    public void Binding_registry_rejects_built_in_capability_with_unrelated_effects()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            requiredCapability: "file.read",
            safety: BindingSafety.SideEffectingExternal,
            auditLevel: AuditLevel.PerCall)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-CAP-EFFECT");
    }

    [Fact]
    public void Binding_registry_composes_validators_for_shared_capability()
    {
        var registry = new BindingRegistryBuilder()
            .Add(TestBinding(
                id: "test.first",
                effects: SandboxEffect.HostStateRead | SandboxEffect.Audit,
                requiredCapability: "game.read",
                safety: BindingSafety.ReadOnlyExternal,
                auditLevel: AuditLevel.PerCall,
                grantValidator: (_, diagnostics) => diagnostics.Add(new SandboxDiagnostic("FIRST", "first"))))
            .Add(TestBinding(
                id: "test.second",
                effects: SandboxEffect.HostStateRead | SandboxEffect.Audit,
                requiredCapability: "game.read",
                safety: BindingSafety.ReadOnlyExternal,
                auditLevel: AuditLevel.PerCall,
                grantValidator: (_, diagnostics) => diagnostics.Add(new SandboxDiagnostic("SECOND", "second"))))
            .Build();
        var diagnostics = new List<SandboxDiagnostic>();

        Assert.True(registry.TryGetCapabilityGrantValidator("game.read", out var validator));
        validator(new CapabilityGrant("game.read", new Dictionary<string, string>()), diagnostics);

        Assert.Contains(diagnostics, d => d.Code == "FIRST");
        Assert.Contains(diagnostics, d => d.Code == "SECOND");
    }

    [Theory]
    [InlineData("F32")]
    [InlineData("Decimal")]
    [InlineData("Bytes")]
    [InlineData("Command")]
    public void Binding_registry_rejects_unsupported_scalar_types(string typeName)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            parameters: [SandboxType.Scalar(typeName)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Dynamic")]
    [InlineData("Type")]
    [InlineData("IServiceProvider")]
    [InlineData("Stream")]
    [InlineData("HttpClient")]
    [InlineData("DbContext")]
    public void Binding_registry_rejects_forbidden_parameter_types(string typeName)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            parameters: [SandboxType.Scalar(typeName)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Type")]
    [InlineData("Delegate")]
    [InlineData("Stream")]
    [InlineData("HttpClient")]
    [InlineData("RawDomainEntity")]
    public void Binding_registry_rejects_forbidden_return_types(string typeName)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            returnType: SandboxType.Scalar(typeName))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Fact]
    public void Binding_registry_rejects_non_hashable_map_key_type()
    {
        var type = SandboxType.Map(SandboxType.List(SandboxType.I32), SandboxType.I32);

        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(parameters: [type])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    private static BindingRegistry Build(BindingDescriptor binding)
        => new BindingRegistryBuilder().Add(binding).Build();

    private static BindingDescriptor TestBinding(
        string id = "test.binding",
        SandboxEffect effects = SandboxEffect.Cpu,
        string? requiredCapability = null,
        BindingSafety safety = BindingSafety.PureHostFacade,
        AuditLevel auditLevel = AuditLevel.None,
        CapabilityGrantValidator? grantValidator = null,
        IReadOnlyList<SandboxType>? parameters = null,
        SandboxType? returnType = null)
        => new(
            id,
            SemVersion.One,
            parameters ?? [],
            returnType ?? SandboxType.Unit,
            effects,
            requiredCapability,
            BindingCostModel.Fixed(1),
            auditLevel,
            safety,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            grantValidator);

    private static void NoParameters(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter '{key}' is not supported"));
        }
    }
}
