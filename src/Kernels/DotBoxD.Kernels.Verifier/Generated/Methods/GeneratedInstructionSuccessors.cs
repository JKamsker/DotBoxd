using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

internal static class GeneratedInstructionSuccessors
{
    public static SuccessorSet For(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, int> indexByOffset,
        GeneratedInstruction instruction)
    {
        if (instruction.Opcode == ILOpCode.Ret)
        {
            return SuccessorSet.Empty;
        }

        if (instruction.Opcode is ILOpCode.Br or ILOpCode.Br_s)
        {
            return instruction.BranchTarget is { } target ? SuccessorSet.One(target) : SuccessorSet.Empty;
        }

        if (instruction.Opcode == ILOpCode.Switch)
        {
            return SwitchSuccessors(instructions, indexByOffset, instruction);
        }

        if (instruction.Opcode.IsBranch())
        {
            return BranchSuccessors(instructions, indexByOffset, instruction);
        }

        return NextInstructionOffset(instructions, indexByOffset, instruction) is { } fallthrough
            ? SuccessorSet.One(fallthrough)
            : SuccessorSet.Empty;
    }

    private static SuccessorSet BranchSuccessors(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, int> indexByOffset,
        GeneratedInstruction instruction)
    {
        if (instruction.BranchTarget is not { } target)
        {
            return NextInstructionOffset(instructions, indexByOffset, instruction) is { } fallthrough
                ? SuccessorSet.One(fallthrough)
                : SuccessorSet.Empty;
        }

        var next = NextInstructionOffset(instructions, indexByOffset, instruction);
        return next is { } nextOffset ? SuccessorSet.Two(target, nextOffset) : SuccessorSet.One(target);
    }

    private static SuccessorSet SwitchSuccessors(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, int> indexByOffset,
        GeneratedInstruction instruction)
    {
        var targets = instruction.SwitchTargets;
        var next = NextInstructionOffset(instructions, indexByOffset, instruction);
        var count = targets.Count + (next.HasValue ? 1 : 0);
        if (count == 0)
        {
            return SuccessorSet.Empty;
        }

        if (count == 1)
        {
            return SuccessorSet.One(targets.Count == 1 ? targets[0] : next.GetValueOrDefault());
        }

        if (count == 2)
        {
            return SuccessorSet.Two(targets[0], targets.Count == 2 ? targets[1] : next.GetValueOrDefault());
        }

        var successors = new int[count];
        for (var i = 0; i < targets.Count; i++)
        {
            successors[i] = targets[i];
        }

        if (next.HasValue)
        {
            successors[^1] = next.Value;
        }

        return SuccessorSet.From(successors);
    }

    private static int? NextInstructionOffset(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, int> indexByOffset,
        GeneratedInstruction instruction)
    {
        if (indexByOffset.ContainsKey(instruction.NextOffset))
        {
            return instruction.NextOffset;
        }

        if (indexByOffset.TryGetValue(instruction.Offset, out var index) &&
            index + 1 < instructions.Count)
        {
            return instructions[index + 1].Offset;
        }

        return null;
    }
}
