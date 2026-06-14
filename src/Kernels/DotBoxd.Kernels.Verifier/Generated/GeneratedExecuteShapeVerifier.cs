namespace DotBoxd.Kernels.Verifier;

using System.Reflection.Metadata;
using static DotBoxd.Kernels.Verifier.GeneratedMethodShapeSignatures;

internal static class GeneratedExecuteShapeVerifier
{
    public static void Verify(GeneratedMethodFlow analysis, List<VerificationDiagnostic> diagnostics)
    {
        RequireReachable(analysis, ValidateInput, diagnostics, "Execute must validate entrypoint input shape");
        RequireReachable(analysis, GeneratedMeterState.LocalFunctionCall, diagnostics, "Execute must dispatch to a generated function");
        RequireReturns(
            analysis,
            GeneratedMeterState.ValidateInput | GeneratedMeterState.LocalFunctionCall,
            diagnostics,
            "Execute must validate input and dispatch before returning");
        if (analysis.HasUnmeteredCycle || analysis.Instructions.Any(i => i.Opcode.IsBranch()))
        {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", "Execute must not contain control-flow branches"));
        }

        VerifyReturnShape(analysis, diagnostics);
        VerifyCalls(analysis, diagnostics);
    }

    private static void VerifyCalls(GeneratedMethodFlow analysis, List<VerificationDiagnostic> diagnostics)
    {
        foreach (var instruction in analysis.Instructions.Where(i => i.CalledMember is not null))
        {
            if (instruction.IsLocalCall)
            {
                VerifyLocalCall(analysis, instruction, diagnostics);
            }
            else if (!ExecuteAllowedCalls.Contains(instruction.CalledMember!))
            {
                diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", "Execute may only validate input and dispatch"));
            }
        }
    }

    private static void VerifyLocalCall(
        GeneratedMethodFlow analysis,
        GeneratedInstruction instruction,
        List<VerificationDiagnostic> diagnostics)
    {
        if (!IsGeneratedFunctionCall(instruction.CalledMember))
        {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", "Execute may only call generated function helpers"));
        }

        var state = analysis.EntryStates.TryGetValue(instruction.Offset, out var entryState)
            ? entryState
            : GeneratedMeterState.None;
        if ((state & GeneratedMeterState.ValidateInput) == 0)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                "Execute must validate input before dispatching to a generated function"));
        }
    }

    private static void VerifyReturnShape(
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        for (var i = 0; i < analysis.Instructions.Count; i++)
        {
            var instruction = analysis.Instructions[i];
            if (instruction.Opcode != ILOpCode.Ret || !analysis.EntryStates.ContainsKey(instruction.Offset))
            {
                continue;
            }

            var previous = i > 0 ? analysis.Instructions[i - 1] : null;
            if (previous is null ||
                !previous.IsLocalCall ||
                !IsGeneratedFunctionCall(previous.CalledMember))
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    "Execute must directly return the generated function result"));
            }
        }
    }

    private static void RequireReachable(
        GeneratedMethodFlow analysis,
        string signature,
        List<VerificationDiagnostic> diagnostics,
        string message)
    {
        if (!analysis.ReachableCalls.Contains(signature))
        {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
        }
    }

    private static void RequireReachable(
        GeneratedMethodFlow analysis,
        GeneratedMeterState required,
        List<VerificationDiagnostic> diagnostics,
        string message)
    {
        if (analysis.ReturnStates.Count == 0 ||
            analysis.ReturnStates.All(state => (state & required) != required))
        {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
        }
    }

    private static void RequireReturns(
        GeneratedMethodFlow analysis,
        GeneratedMeterState required,
        List<VerificationDiagnostic> diagnostics,
        string message)
    {
        if (analysis.ReturnStates.Count == 0 ||
            analysis.ReturnStates.Any(state => (state & required) != required))
        {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
        }
    }
}
