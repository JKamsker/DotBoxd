namespace DotBoxd.Kernels.Verifier;

using System.Reflection.Metadata;

internal sealed record GeneratedMethodFlow(
    IReadOnlyList<GeneratedInstruction> Instructions,
    IReadOnlyDictionary<int, GeneratedInstruction> ByOffset,
    IReadOnlyDictionary<int, int> IndexByOffset,
    IReadOnlyDictionary<int, SuccessorSet> SuccessorsByOffset,
    IReadOnlyDictionary<int, IReadOnlyList<GeneratedInstruction>> PredecessorsByOffset,
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
    bool IsLocalCall,
    EntityHandle? OperandHandle,
    int? OperandIndex,
    int? Int32Value);

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
