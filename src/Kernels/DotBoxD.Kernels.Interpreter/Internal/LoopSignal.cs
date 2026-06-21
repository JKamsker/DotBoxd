using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

/// <summary>
/// Sentinel <see cref="SandboxValue"/>s that unwind structured loop control
/// (<c>continue</c>/<c>break</c>) out of nested blocks and conditionals, reusing the
/// existing non-null "early exit" signal that <c>return</c> already relies on in
/// <see cref="StatementExecutor.ExecuteBlockAsync"/>. Blocks and <c>if</c> branches
/// propagate them upward unchanged; a loop runner recognises them by reference and stops
/// the propagation — <c>continue</c> advances to the next iteration, <c>break</c> exits the
/// loop. They never escape a loop because the validator rejects <c>continue</c>/<c>break</c>
/// outside one (E-LOOP-CONTROL), so they never reach the function-level return check.
/// </summary>
internal static class LoopSignal
{
    public static SandboxValue Continue { get; } = new LoopControlValue(IsBreak: false);

    public static SandboxValue Break { get; } = new LoopControlValue(IsBreak: true);

    public static bool IsContinue(SandboxValue? value) => ReferenceEquals(value, Continue);

    public static bool IsBreak(SandboxValue? value) => ReferenceEquals(value, Break);

    private sealed record LoopControlValue(bool IsBreak) : SandboxValue
    {
        public override SandboxType Type => SandboxType.Unit;
    }
}
