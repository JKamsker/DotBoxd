using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.TestFixtures.MergeableIr;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Proves the runtime composer turns generator-produced <see cref="LoweredPipelineStep"/> fragments into a
/// real, verifiable, interpretable <see cref="SandboxModule"/> — the "combine at runtime" counterpart to the
/// build-time hook-chain fusion. The steps come from the same fixture the source generator lowered.
/// </summary>
public sealed class LoweredPipelineComposerTests
{
    [Fact]
    public async Task Composes_fixture_steps_into_a_verifiable_runnable_module()
    {
        // Where(Distance >= 4).Select(TargetId), lowered by the generator into two mergeable-IR fragments.
        var steps = MergeableIrPipelineFixture.ConfigureSteps();

        var module = LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("mergeable-pipeline", steps, SandboxType.String));

        var host = SandboxTestHost.Create();
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000_000).Build());

        var pass = Record(5, "target-1");
        var fail = Record(3, "target-2");

        var gateTrue = await host.ExecuteAsync(plan, "ShouldHandle", pass);
        var gateFalse = await host.ExecuteAsync(plan, "ShouldHandle", fail);
        var projected = await host.ExecuteAsync(plan, "Handle", pass);

        Assert.True(gateTrue.Succeeded, gateTrue.Error?.SafeMessage);
        Assert.True(((BoolValue)gateTrue.Value!).Value);
        Assert.False(((BoolValue)gateFalse.Value!).Value);
        Assert.Equal("target-1", ((StringValue)projected.Value!).Value);
    }

    [Fact]
    public void Emits_two_entrypoints_and_surfaces_step_capabilities_in_metadata()
    {
        var steps = MergeableIrPipelineFixture.ConfigureSteps();

        var module = LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("mergeable-pipeline", steps, SandboxType.String));

        Assert.Equal(2, module.Functions.Count);
        Assert.All(module.Functions, function => Assert.True(function.IsEntrypoint));
        Assert.Contains(module.Functions, f => f.Id == "ShouldHandle" && f.ReturnType == SandboxType.Bool);
        Assert.Contains(module.Functions, f => f.Id == "Handle" && f.ReturnType == SandboxType.String);
        Assert.Empty(module.CapabilityRequests);
        Assert.Equal("probe.read.distance", module.Metadata["dotboxd.requiredCapabilities"]);
    }

    [Fact]
    public void Rejects_an_empty_composition()
        => Assert.Throws<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("empty", [], SandboxType.I32)));

    [Fact]
    public void Rejects_steps_whose_shapes_do_not_flow()
    {
        // A projection I32 -> String followed by a filter that expects I32: the shapes do not chain.
        var projection = Step(LoweredPipelineStepKind.Projection, "i32", "string", SandboxType.I32);
        var filter = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32);

        var mismatch = Assert.Throws<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("mismatch", [projection, filter], SandboxType.String)));
        Assert.Contains("does not match", mismatch.Message, StringComparison.Ordinal);
    }

    private static LoweredPipelineStep Step(
        LoweredPipelineStepKind kind,
        string inputTag,
        string outputTag,
        SandboxType inputType)
    {
        var span = new SourceSpan(1, 1);
        Expression value = kind == LoweredPipelineStepKind.Filter
            ? new LiteralExpression(SandboxValue.FromBool(true), span)
            : new VariableExpression("$dotboxd.current", span);
        return new LoweredPipelineStep(
            kind,
            inputTag,
            outputTag,
            [new Parameter("$dotboxd.current", inputType)],
            [],
            value,
            [],
            []);
    }

    // A single-parameter entrypoint receives the value directly (no positional-argument list wrapper).
    private static SandboxValue Record(int distance, string targetId)
        => SandboxValue.FromRecord([SandboxValue.FromInt32(distance), SandboxValue.FromString(targetId)]);
}
