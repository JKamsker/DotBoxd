using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

/// <summary>
/// Inputs for <see cref="LoweredPipelineComposer.Compose"/>. Carries the ordered mergeable-IR
/// <see cref="LoweredPipelineStep"/> fragments plus the small amount of module identity a fragment
/// deliberately does not know (see <c>docs/design/mergeable-ir-fragments</c>).
/// </summary>
public sealed record LoweredPipelineComposition(
    string ModuleId,
    IReadOnlyList<LoweredPipelineStep> Steps,
    SandboxType ResultType)
{
    private IReadOnlyList<LoweredPipelineStep> _steps = CopySteps(Steps);

    public IReadOnlyList<LoweredPipelineStep> Steps
    {
        get => _steps;
        init => _steps = CopySteps(value);
    }

    /// <summary>Module version stamped on the composed module.</summary>
    public SemVersion Version { get; init; } = new(1, 0, 0);

    /// <summary>Sandbox ABI the composed module targets.</summary>
    public SemVersion TargetSandboxVersion { get; init; } = new(1, 0, 0);

    /// <summary>Id of the emitted boolean gate function (AND of every filter).</summary>
    public string ShouldHandleFunctionId { get; init; } = "ShouldHandle";

    /// <summary>Id of the emitted projection function (returns the final projected value).</summary>
    public string HandleFunctionId { get; init; } = "Handle";

    private static IReadOnlyList<LoweredPipelineStep> CopySteps(IEnumerable<LoweredPipelineStep> steps)
        => Array.AsReadOnly((steps ?? throw new ArgumentNullException(nameof(steps))).ToArray());
}
