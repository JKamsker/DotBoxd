namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal static class GeneratedMethodFlowAnalyzer
{
    public static GeneratedMethodFlow Analyze(
        IReadOnlyList<GeneratedInstruction> instructions,
        Func<GeneratedInstruction, GeneratedMeterState> stateFor)
    {
        var byOffset = instructions.ToDictionary(i => i.Offset);
        var indexByOffset = InstructionIndexes(instructions);
        var successorsByOffset = SuccessorMap(instructions, byOffset);
        var predecessorsByOffset = PredecessorMap(instructions, successorsByOffset);
        var reachableCalls = new HashSet<string>(StringComparer.Ordinal);
        var returnStates = new List<GeneratedMeterState>();
        var states = ReachableStates(instructions, byOffset, successorsByOffset, reachableCalls, returnStates, stateFor);
        return new GeneratedMethodFlow(
            instructions,
            byOffset,
            indexByOffset,
            successorsByOffset,
            predecessorsByOffset,
            reachableCalls,
            states,
            returnStates,
            HasUnmeteredCycle(byOffset, successorsByOffset, states.Keys));
    }

    private static Dictionary<int, int> InstructionIndexes(IReadOnlyList<GeneratedInstruction> instructions)
    {
        var indexes = new Dictionary<int, int>(instructions.Count);
        for (var i = 0; i < instructions.Count; i++)
        {
            indexes[instructions[i].Offset] = i;
        }

        return indexes;
    }

    private static Dictionary<int, SuccessorSet> SuccessorMap(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset)
    {
        var successors = new Dictionary<int, SuccessorSet>(instructions.Count);
        var buffer = new List<int>();
        foreach (var instruction in instructions)
        {
            buffer.Clear();
            foreach (var successor in Successors(instructions, byOffset, instruction))
            {
                buffer.Add(successor);
            }

            successors[instruction.Offset] = SuccessorSet.From(buffer);
        }

        return successors;
    }

    private static Dictionary<int, IReadOnlyList<GeneratedInstruction>> PredecessorMap(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, SuccessorSet> successorsByOffset)
    {
        var predecessors = new Dictionary<int, List<GeneratedInstruction>>();
        foreach (var instruction in instructions)
        {
            foreach (var successor in successorsByOffset[instruction.Offset])
            {
                if (!predecessors.TryGetValue(successor, out var current))
                {
                    current = [];
                    predecessors.Add(successor, current);
                }

                current.Add(instruction);
            }
        }

        return predecessors.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<GeneratedInstruction>)item.Value.ToArray());
    }

    public static IEnumerable<int> Successors(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        GeneratedInstruction instruction)
    {
        if (instruction.Opcode == ILOpCode.Ret)
        {
            yield break;
        }

        if (instruction.Opcode is ILOpCode.Br or ILOpCode.Br_s)
        {
            if (instruction.BranchTarget is { } target)
            {
                yield return target;
            }

            yield break;
        }

        if (instruction.Opcode == ILOpCode.Switch)
        {
            foreach (var target in instruction.SwitchTargets)
            {
                yield return target;
            }

            if (NextInstructionOffset(instructions, byOffset, instruction) is { } next)
            {
                yield return next;
            }

            yield break;
        }

        if (instruction.Opcode.IsBranch())
        {
            if (instruction.BranchTarget is { } target)
            {
                yield return target;
            }

            if (NextInstructionOffset(instructions, byOffset, instruction) is { } next)
            {
                yield return next;
            }

            yield break;
        }

        if (NextInstructionOffset(instructions, byOffset, instruction) is { } fallthrough)
        {
            yield return fallthrough;
        }
    }

    private static Dictionary<int, GeneratedMeterState> ReachableStates(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        IReadOnlyDictionary<int, SuccessorSet> successorsByOffset,
        HashSet<string> reachableCalls,
        List<GeneratedMeterState> returnStates,
        Func<GeneratedInstruction, GeneratedMeterState> stateFor)
    {
        var states = new Dictionary<int, GeneratedMeterState>();
        if (instructions.Count == 0)
        {
            return states;
        }

        states[instructions[0].Offset] = GeneratedMeterState.None;
        var queue = new Queue<int>();
        queue.Enqueue(instructions[0].Offset);
        while (queue.Count > 0)
        {
            var offset = queue.Dequeue();
            var instruction = byOffset[offset];
            var output = states[offset] | stateFor(instruction);
            if (instruction.CalledMember is not null)
            {
                reachableCalls.Add(instruction.CalledMember);
            }

            if (instruction.Opcode == ILOpCode.Ret)
            {
                returnStates.Add(output);
                continue;
            }

            foreach (var successor in successorsByOffset[offset])
            {
                if (!byOffset.ContainsKey(successor))
                {
                    continue;
                }

                if (!states.TryGetValue(successor, out var existing))
                {
                    states[successor] = output;
                    queue.Enqueue(successor);
                    continue;
                }

                var joined = existing & output;
                if (joined != existing)
                {
                    states[successor] = joined;
                    queue.Enqueue(successor);
                }
            }
        }

        return states;
    }

    private static bool HasUnmeteredCycle(
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        IReadOnlyDictionary<int, SuccessorSet> successorsByOffset,
        IEnumerable<int> reachableOffsets)
    {
        var reachable = reachableOffsets.ToHashSet();
        var colors = new Dictionary<int, VisitColor>();
        return reachable.Any(offset => Visit(offset, byOffset, successorsByOffset, reachable, colors));
    }

    private static bool Visit(
        int offset,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        IReadOnlyDictionary<int, SuccessorSet> successorsByOffset,
        HashSet<int> reachable,
        Dictionary<int, VisitColor> colors)
    {
        if (!reachable.Contains(offset) || !byOffset.TryGetValue(offset, out var instruction) || IsLoopIterationCharge(instruction))
        {
            return false;
        }

        if (colors.TryGetValue(offset, out var color))
        {
            return color == VisitColor.Visiting;
        }

        colors[offset] = VisitColor.Visiting;
        foreach (var successor in successorsByOffset[offset])
        {
            if (Visit(successor, byOffset, successorsByOffset, reachable, colors))
            {
                return true;
            }
        }

        colors[offset] = VisitColor.Visited;
        return false;
    }

    private static bool IsLoopIterationCharge(GeneratedInstruction instruction)
        => instruction.CalledMember == GeneratedMethodShapeSignatures.ChargeLoopIterationSignature;

    private static int? NextInstructionOffset(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        GeneratedInstruction instruction)
    {
        if (byOffset.ContainsKey(instruction.NextOffset))
        {
            return instruction.NextOffset;
        }

        for (var i = 0; i + 1 < instructions.Count; i++)
        {
            if (instructions[i] == instruction)
            {
                return instructions[i + 1].Offset;
            }
        }

        return null;
    }

    private enum VisitColor
    {
        Visiting,
        Visited
    }
}
