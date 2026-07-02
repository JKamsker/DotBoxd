using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;

internal static class GeneratedUnchargedLiteralShapeVerifier
{
    public static void Verify(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var instruction in analysis.Instructions.Where(i => IsUnchargedLiteralCall(i.CalledMember)))
        {
            if (!IsReachable(analysis, instruction) ||
                IsImmediatelyStoredIntoLiteralArray(analysis, instruction) ||
                IsStoredIntoLocalChargedBeforeReturn(analysis, instruction))
            {
                continue;
            }

            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must store uncharged literal helper '{instruction.CalledMember}' directly into a literal array"));
        }
    }

    private static bool IsImmediatelyStoredIntoLiteralArray(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction)
    {
        if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) ||
            index + 1 >= analysis.Instructions.Count)
        {
            return false;
        }

        return analysis.Instructions[index + 1].Opcode == ILOpCode.Stelem_ref;
    }

    private static bool IsStoredIntoLocalChargedBeforeReturn(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction)
    {
        if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) ||
            index + 1 >= analysis.Instructions.Count ||
            !IsStoreLocal(analysis.Instructions[index + 1], out var localIndex))
        {
            return false;
        }

        var store = analysis.Instructions[index + 1];
        var queue = new Queue<ChargeSearchState>();
        var visited = new HashSet<ChargeSearchState>();
        EnqueueSuccessors(analysis, store, charged: false, queue, visited);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (!analysis.ByOffset.TryGetValue(state.Offset, out var candidate) ||
                !IsReachable(analysis, candidate))
            {
                continue;
            }

            var currentIndex = analysis.IndexByOffset[candidate.Offset];
            var charged = state.Charged ||
                IsChargeSandboxValueCall(candidate) &&
                PreviousInstructionsLoadLocal(analysis.Instructions, currentIndex, localIndex);
            if (candidate.Opcode == ILOpCode.Ret)
            {
                if (!charged &&
                    PreviousInstructionLoadsLocal(analysis.Instructions, currentIndex, localIndex))
                {
                    return false;
                }

                continue;
            }

            EnqueueSuccessors(analysis, candidate, charged, queue, visited);
        }

        return true;
    }

    private static void EnqueueSuccessors(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction,
        bool charged,
        Queue<ChargeSearchState> queue,
        HashSet<ChargeSearchState> visited)
    {
        if (!analysis.SuccessorsByOffset.TryGetValue(instruction.Offset, out var successors))
        {
            return;
        }

        foreach (var successor in successors)
        {
            var state = new ChargeSearchState(successor, charged);
            if (visited.Add(state))
            {
                queue.Enqueue(state);
            }
        }
    }

    private static bool IsChargeSandboxValueCall(GeneratedInstruction instruction)
        => instruction.CalledMember == ChargeSandboxValueSignature ||
           instruction.CalledMember == ChargeSandboxValuesSignature;

    private static bool PreviousInstructionsLoadLocal(
        IReadOnlyList<GeneratedInstruction> instructions,
        int index,
        int localIndex)
    {
        var first = Math.Max(0, index - 3);
        for (var current = first; current < index; current++)
        {
            if (IsLoadLocal(instructions[current], localIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PreviousInstructionLoadsLocal(
        IReadOnlyList<GeneratedInstruction> instructions,
        int index,
        int localIndex)
        => index > 0 && IsLoadLocal(instructions[index - 1], localIndex);

    private static bool IsReachable(GeneratedMethodFlow analysis, GeneratedInstruction instruction)
        => analysis.EntryStates.ContainsKey(instruction.Offset);

    private static bool IsLoadLocal(GeneratedInstruction instruction, int localIndex)
        => (instruction.Opcode is ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloc_0
                or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3) &&
            instruction.OperandIndex == localIndex;

    private static bool IsStoreLocal(GeneratedInstruction instruction, out int localIndex)
    {
        if ((instruction.Opcode is ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Stloc_0
                or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3) &&
            instruction.OperandIndex is { } index)
        {
            localIndex = index;
            return true;
        }

        localIndex = -1;
        return false;
    }

    private readonly record struct ChargeSearchState(int Offset, bool Charged);
}
