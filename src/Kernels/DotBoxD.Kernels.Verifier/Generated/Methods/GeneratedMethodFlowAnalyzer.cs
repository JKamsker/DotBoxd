using System.Reflection.Metadata;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

internal static class GeneratedMethodFlowAnalyzer
{
    public static GeneratedMethodFlow Analyze(
        IReadOnlyList<GeneratedInstruction> instructions,
        Func<GeneratedInstruction, GeneratedMeterState> stateFor)
    {
        var byOffset = new Dictionary<int, GeneratedInstruction>(instructions.Count);
        var indexByOffset = new Dictionary<int, int>(instructions.Count);
        var hasBranches = false;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            byOffset.Add(instruction.Offset, instruction);
            indexByOffset.Add(instruction.Offset, i);
            hasBranches |= instruction.Opcode.IsBranch();
        }

        if (!hasBranches)
        {
            return AnalyzeLinear(instructions, byOffset, indexByOffset, stateFor);
        }

        var successorsByOffset = SuccessorMap(instructions, indexByOffset);
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
            HasUnmeteredCycle(byOffset, successorsByOffset, states));
    }

    private static GeneratedMethodFlow AnalyzeLinear(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        IReadOnlyDictionary<int, int> indexByOffset,
        Func<GeneratedInstruction, GeneratedMeterState> stateFor)
    {
        var successorsByOffset = LinearSuccessorMap(instructions, indexByOffset);
        var predecessorsByOffset = PredecessorMap(instructions, successorsByOffset);
        var reachableCalls = new HashSet<string>(StringComparer.Ordinal);
        var entryStates = new Dictionary<int, GeneratedMeterState>();
        var returnStates = new List<GeneratedMeterState>();
        var state = GeneratedMeterState.None;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            entryStates[instruction.Offset] = state;
            state |= stateFor(instruction);
            if (instruction.CalledMember is not null)
            {
                reachableCalls.Add(instruction.CalledMember);
            }

            if (instruction.Opcode == ILOpCode.Ret)
            {
                returnStates.Add(state);
                break;
            }
        }

        return new GeneratedMethodFlow(
            instructions,
            byOffset,
            indexByOffset,
            successorsByOffset,
            predecessorsByOffset,
            reachableCalls,
            entryStates,
            returnStates,
            HasUnmeteredCycle: false);
    }

    private static Dictionary<int, SuccessorSet> LinearSuccessorMap(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, int> indexByOffset)
    {
        var successors = new Dictionary<int, SuccessorSet>(instructions.Count);
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.Opcode == ILOpCode.Ret)
            {
                successors[instruction.Offset] = SuccessorSet.Empty;
                continue;
            }

            if (indexByOffset.ContainsKey(instruction.NextOffset))
            {
                successors[instruction.Offset] = SuccessorSet.One(instruction.NextOffset);
                continue;
            }

            successors[instruction.Offset] = i + 1 < instructions.Count
                ? SuccessorSet.One(instructions[i + 1].Offset)
                : SuccessorSet.Empty;
        }

        return successors;
    }

    private static Dictionary<int, SuccessorSet> SuccessorMap(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, int> indexByOffset)
    {
        var successors = new Dictionary<int, SuccessorSet>(instructions.Count);
        foreach (var instruction in instructions)
        {
            successors[instruction.Offset] = GeneratedInstructionSuccessors.For(
                instructions,
                indexByOffset,
                instruction);
        }

        return successors;
    }

    private static Dictionary<int, PredecessorSummary> PredecessorMap(
        IReadOnlyList<GeneratedInstruction> instructions,
        IReadOnlyDictionary<int, SuccessorSet> successorsByOffset)
    {
        var predecessors = new Dictionary<int, PredecessorSummary>();
        foreach (var instruction in instructions)
        {
            foreach (var successor in successorsByOffset[instruction.Offset])
            {
                predecessors.TryGetValue(successor, out var current);
                predecessors[successor] = current.Add(instruction);
            }
        }

        return predecessors;
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
        IReadOnlyDictionary<int, GeneratedMeterState> reachableStates)
    {
        var colors = new Dictionary<int, VisitColor>(reachableStates.Count);
        foreach (var offset in reachableStates.Keys)
        {
            if (Visit(offset, byOffset, successorsByOffset, reachableStates, colors))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Visit(
        int offset,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        IReadOnlyDictionary<int, SuccessorSet> successorsByOffset,
        IReadOnlyDictionary<int, GeneratedMeterState> reachableStates,
        Dictionary<int, VisitColor> colors)
    {
        if (!IsTraversable(offset, byOffset, reachableStates))
        {
            return false;
        }

        if (colors.TryGetValue(offset, out var existing))
        {
            return existing == VisitColor.Visiting;
        }

        var stack = new Stack<FlowVisitFrame>();
        colors[offset] = VisitColor.Visiting;
        stack.Push(new FlowVisitFrame(offset, NextSuccessorIndex: 0));

        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            var successors = successorsByOffset[frame.Offset];
            if (frame.NextSuccessorIndex < successors.Count)
            {
                var successor = successors[frame.NextSuccessorIndex];
                stack.Push(frame with { NextSuccessorIndex = frame.NextSuccessorIndex + 1 });

                if (!IsTraversable(successor, byOffset, reachableStates))
                {
                    continue;
                }

                if (colors.TryGetValue(successor, out var color))
                {
                    if (color == VisitColor.Visiting)
                    {
                        return true;
                    }

                    continue;
                }

                colors[successor] = VisitColor.Visiting;
                stack.Push(new FlowVisitFrame(successor, NextSuccessorIndex: 0));
                continue;
            }

            colors[frame.Offset] = VisitColor.Visited;
        }

        return false;
    }

    private static bool IsTraversable(
        int offset,
        IReadOnlyDictionary<int, GeneratedInstruction> byOffset,
        IReadOnlyDictionary<int, GeneratedMeterState> reachableStates)
        => reachableStates.ContainsKey(offset)
            && byOffset.TryGetValue(offset, out var instruction)
            && !IsLoopIterationCharge(instruction);

    private static bool IsLoopIterationCharge(GeneratedInstruction instruction)
        => instruction.CalledMember == GeneratedMethodShapeSignatures.ChargeLoopIterationSignature;

    private readonly record struct FlowVisitFrame(int Offset, int NextSuccessorIndex);
}
