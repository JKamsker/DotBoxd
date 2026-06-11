namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class GeneratedMethodShapeVerifier
{
    private const string Runtime = "SafeIR.Runtime.CompiledRuntime";
    private const string Context = "SafeIR.SandboxContext";
    private const string Value = "SafeIR.SandboxValue";
    private const string Int32 = "System.Int32";
    private const string Void = "System.Void";
    private const string ValidateInput = $"{Runtime}.ValidateEntrypointInput({Value},{Int32}):{Void}";
    private const string EnterCall = $"{Runtime}.EnterCall({Context}):{Void}";
    private const string ExitCall = $"{Runtime}.ExitCall({Context}):{Void}";
    private const string ChargeFuel = $"{Runtime}.ChargeFuel({Context},{Int32}):{Void}";

    public static void VerifyBody(
        MetadataReader reader,
        MethodBodyBlock body,
        string methodName,
        List<VerificationDiagnostic> diagnostics)
    {
        var analysis = Analyze(reader, body);
        if (methodName == "Execute") {
            RequireReachable(analysis, ValidateInput, diagnostics, "Execute must validate entrypoint input shape");
            RequireReturns(analysis, MeterState.ValidateInput, diagnostics, "Execute must validate entrypoint input shape before returning");
            return;
        }

        if (methodName.StartsWith("Fn_", StringComparison.Ordinal)) {
            RequireReachable(analysis, EnterCall, diagnostics, $"method '{methodName}' must enter the call meter");
            RequireReachable(analysis, ExitCall, diagnostics, $"method '{methodName}' must exit the call meter");
            RequireReachable(analysis, ChargeFuel, diagnostics, $"method '{methodName}' must charge fuel");
            RequireReturns(
                analysis,
                MeterState.EnterCall | MeterState.ExitCall | MeterState.ChargeFuel,
                diagnostics,
                $"method '{methodName}' must meter every return path");
        }
    }

    private static FlowAnalysis Analyze(MetadataReader reader, MethodBodyBlock body)
    {
        var instructions = ReadInstructions(reader, body);
        var byOffset = instructions.ToDictionary(i => i.Offset);
        var reachableCalls = new HashSet<string>(StringComparer.Ordinal);
        var returnStates = new List<MeterState>();
        if (instructions.Count == 0) {
            return new FlowAnalysis(reachableCalls, returnStates);
        }

        var states = new Dictionary<int, MeterState> { [instructions[0].Offset] = MeterState.None };
        var queue = new Queue<int>();
        queue.Enqueue(instructions[0].Offset);
        while (queue.Count > 0) {
            var offset = queue.Dequeue();
            var instruction = byOffset[offset];
            var output = states[offset] | StateFor(instruction.CalledMember);
            if (instruction.CalledMember is not null) {
                reachableCalls.Add(instruction.CalledMember);
            }

            if (instruction.Opcode == ILOpCode.Ret) {
                returnStates.Add(output);
                continue;
            }

            foreach (var successor in Successors(instructions, byOffset, instruction)) {
                if (!byOffset.ContainsKey(successor)) {
                    continue;
                }

                if (!states.TryGetValue(successor, out var existing)) {
                    states[successor] = output;
                    queue.Enqueue(successor);
                    continue;
                }

                var joined = existing & output;
                if (joined != existing) {
                    states[successor] = joined;
                    queue.Enqueue(successor);
                }
            }
        }

        return new FlowAnalysis(reachableCalls, returnStates);
    }

    private static List<Instruction> ReadInstructions(MetadataReader reader, MethodBodyBlock body)
    {
        var instructions = new List<Instruction>();
        var il = body.GetILReader();
        while (il.RemainingBytes > 0) {
            var offset = il.Offset;
            var opcode = ReadOpCode(ref il);
            string? calledMember = null;
            int? branchTarget = null;
            int[] switchTargets = [];
            if (opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj) {
                var handle = MetadataTokens.EntityHandle(il.ReadInt32());
                calledMember = MetadataName.MemberSignature(reader, handle).Signature;
            }
            else if (opcode == ILOpCode.Switch) {
                var count = il.ReadInt32();
                var deltas = new int[count];
                for (var i = 0; i < count; i++) {
                    deltas[i] = il.ReadInt32();
                }

                var nextOffset = il.Offset;
                switchTargets = deltas.Select(delta => nextOffset + delta).ToArray();
            }
            else if (opcode.IsBranch()) {
                var delta = opcode.GetBranchOperandSize() == 1 ? il.ReadSByte() : il.ReadInt32();
                branchTarget = il.Offset + delta;
            }
            else {
                SkipOperand(opcode, ref il);
            }

            instructions.Add(new Instruction(offset, opcode, il.Offset, branchTarget, switchTargets, calledMember));
        }

        return instructions;
    }

    private static IEnumerable<int> Successors(
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, Instruction> byOffset,
        Instruction instruction)
    {
        if (instruction.Opcode == ILOpCode.Ret) {
            yield break;
        }

        if (instruction.Opcode is ILOpCode.Br or ILOpCode.Br_s) {
            if (instruction.BranchTarget is { } target) {
                yield return target;
            }

            yield break;
        }

        if (instruction.Opcode == ILOpCode.Switch) {
            foreach (var target in instruction.SwitchTargets) {
                yield return target;
            }

            if (NextInstructionOffset(instructions, byOffset, instruction) is { } next) {
                yield return next;
            }

            yield break;
        }

        if (instruction.Opcode.IsBranch()) {
            if (instruction.BranchTarget is { } target) {
                yield return target;
            }

            if (NextInstructionOffset(instructions, byOffset, instruction) is { } next) {
                yield return next;
            }

            yield break;
        }

        if (NextInstructionOffset(instructions, byOffset, instruction) is { } fallthrough) {
            yield return fallthrough;
        }
    }

    private static int? NextInstructionOffset(
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, Instruction> byOffset,
        Instruction instruction)
    {
        if (byOffset.ContainsKey(instruction.NextOffset)) {
            return instruction.NextOffset;
        }

        for (var i = 0; i + 1 < instructions.Count; i++) {
            if (instructions[i] == instruction) {
                return instructions[i + 1].Offset;
            }
        }

        return null;
    }

    private static MeterState StateFor(string? calledMember)
        => calledMember switch {
            ValidateInput => MeterState.ValidateInput,
            EnterCall => MeterState.EnterCall,
            ExitCall => MeterState.ExitCall,
            ChargeFuel => MeterState.ChargeFuel,
            _ => MeterState.None
        };

    private static void RequireReachable(
        FlowAnalysis analysis,
        string signature,
        List<VerificationDiagnostic> diagnostics,
        string message)
    {
        if (!analysis.ReachableCalls.Contains(signature)) {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
        }
    }

    private static void RequireReturns(
        FlowAnalysis analysis,
        MeterState required,
        List<VerificationDiagnostic> diagnostics,
        string message)
    {
        if (analysis.ReturnStates.Count == 0 ||
            analysis.ReturnStates.Any(state => (state & required) != required)) {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader il)
    {
        var first = il.ReadByte();
        return first == 0xFE ? (ILOpCode)(0xFE00 | il.ReadByte()) : (ILOpCode)first;
    }

    private static void SkipOperand(ILOpCode opcode, ref BlobReader il)
    {
        if (opcode == ILOpCode.Switch) {
            var count = il.ReadInt32();
            for (var i = 0; i < count; i++) {
                _ = il.ReadInt32();
            }

            return;
        }

        if (opcode.IsBranch()) {
            if (opcode.GetBranchOperandSize() == 1) {
                _ = il.ReadSByte();
            }
            else {
                _ = il.ReadInt32();
            }

            return;
        }

        switch (opcode) {
            case ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Stloc:
                _ = il.ReadUInt16();
                break;
            case ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s:
                _ = il.ReadByte();
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldstr or ILOpCode.Newarr:
                _ = il.ReadInt32();
                break;
            case ILOpCode.Ldc_r8:
                _ = il.ReadDouble();
                break;
        }
    }

    private sealed record FlowAnalysis(HashSet<string> ReachableCalls, IReadOnlyList<MeterState> ReturnStates);

    private sealed record Instruction(
        int Offset,
        ILOpCode Opcode,
        int NextOffset,
        int? BranchTarget,
        IReadOnlyList<int> SwitchTargets,
        string? CalledMember);

    [Flags]
    private enum MeterState
    {
        None = 0,
        ValidateInput = 1,
        EnterCall = 2,
        ExitCall = 4,
        ChargeFuel = 8
    }
}
