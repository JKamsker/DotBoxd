namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal sealed record GeneratedMethodFlow(
    IReadOnlyList<GeneratedInstruction> Instructions,
    IReadOnlyDictionary<int, GeneratedInstruction> ByOffset,
    HashSet<string> ReachableCalls,
    IReadOnlyDictionary<int, GeneratedMeterState> EntryStates,
    IReadOnlyList<GeneratedMeterState> ReturnStates,
    bool HasUnmeteredCycle);

internal sealed record GeneratedInstruction(
    int Offset,
    ILOpCode Opcode,
    int NextOffset,
    int? BranchTarget,
    IReadOnlyList<int> SwitchTargets,
    string? CalledMember,
    bool IsLocalCall);

[Flags]
internal enum GeneratedMeterState
{
    None = 0,
    ValidateInput = 1,
    EnterCall = 2,
    ExitCall = 4,
    ChargeFuel = 8,
    LocalFunctionCall = 16
}
