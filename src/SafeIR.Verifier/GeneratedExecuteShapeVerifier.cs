namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal static class GeneratedExecuteShapeVerifier
{
    private const string Runtime = "SafeIR.Runtime.CompiledRuntime";
    private const string Value = "SafeIR.SandboxValue";
    private const string Int32 = "System.Int32";
    private const string Void = "System.Void";
    private const string SandboxType = "SafeIR.SandboxType";
    private static readonly string ValidateInput = $"{Runtime}.ValidateEntrypointInput({Value},{Int32}):{Void}";
    private static readonly string TypeScalar = $"{Runtime}.TypeScalar(System.String):{SandboxType}";
    private static readonly string TypeList = $"{Runtime}.TypeList({SandboxType}):{SandboxType}";
    private static readonly string TypeMap = $"{Runtime}.TypeMap({SandboxType},{SandboxType}):{SandboxType}";

    private static readonly HashSet<string> AllowedCalls = new(StringComparer.Ordinal) {
        ValidateInput,
        $"{Runtime}.GetInputArgument({Value},{Int32},{Int32},{SandboxType}):{Value}",
        TypeScalar,
        TypeList,
        TypeMap
    };

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
            else if (!AllowedCalls.Contains(instruction.CalledMember!))
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

    private static bool IsGeneratedFunctionCall(string? calledMember)
        => calledMember is not null && calledMember.StartsWith("Fn_", StringComparison.Ordinal);

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
