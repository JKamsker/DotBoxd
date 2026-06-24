using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServicePropertyBindingTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task AddBindingsFrom_registers_explicit_host_binding_properties()
    {
        using var host = SandboxHost.Create(
            builder => builder.AddBindingsFrom<IPropertyProbeWorld>(new PropertyProbeWorld()));
        var module = PropertyBindingModule();
        var policy = SandboxPolicyBuilder.Create()
            .Grant("probe.read.scalar", new { }, SandboxEffect.HostStateRead)
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(17, ((I32Value)result.Value!).Value);
    }

    private interface IPropertyProbeWorld
    {
        [HostBinding("host.probe.scalar", "probe.read.scalar", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Scalar { get; }
    }

    private sealed class PropertyProbeWorld : IPropertyProbeWorld
    {
        public int Scalar => 17;
    }

    private static SandboxModule PropertyBindingModule()
        => new(
            "property-binding-probe",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new CallExpression("host.probe.scalar", [], null, Span), Span)])
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));
}
