namespace SafeIR.Verifier;

using System.Reflection.Metadata;

internal static class GeneratedMethodShapeVerifier
{
    private const string Runtime = "SafeIR.Runtime.CompiledRuntime";
    private const string Context = "SafeIR.SandboxContext";
    private const string Value = "SafeIR.SandboxValue";
    private const string Int32 = "System.Int32";
    private const string Void = "System.Void";
    private const string SandboxType = "SafeIR.SandboxType";
    private static readonly string ValidateInput = $"{Runtime}.ValidateEntrypointInput({Value},{Int32}):{Void}";
    private static readonly string EnterCall = $"{Runtime}.EnterCall({Context}):{Void}";
    private static readonly string ExitCall = $"{Runtime}.ExitCall({Context}):{Void}";
    private static readonly string RequireValueType = $"{Runtime}.RequireValueType({Value},{SandboxType}):{Value}";
    internal static readonly string ChargeFuelSignature = $"{Runtime}.ChargeFuel({Context},{Int32}):{Void}";
    internal static readonly string ChargeLoopIterationSignature = $"{Runtime}.ChargeLoopIteration({Context},{Int32}):{Void}";

    public static void VerifyBody(
        MetadataReader reader,
        MethodDefinition method,
        IReadOnlyList<GeneratedInstruction> instructions,
        string methodName,
        List<VerificationDiagnostic> diagnostics)
    {
        var analysis = GeneratedMethodFlowAnalyzer.Analyze(instructions, StateFor);
        GeneratedStackVerifier.Verify(reader, method, analysis, diagnostics);
        if (methodName == "Execute")
        {
            GeneratedExecuteShapeVerifier.Verify(analysis, diagnostics);
            return;
        }

        if (methodName.StartsWith("Fn_", StringComparison.Ordinal))
        {
            VerifyFunction(methodName, analysis, diagnostics);
        }
    }

    private static void VerifyFunction(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        RequireReachable(analysis, EnterCall, diagnostics, $"method '{methodName}' must enter the call meter");
        RequireReachable(analysis, ExitCall, diagnostics, $"method '{methodName}' must exit the call meter");
        RequireReachable(analysis, ChargeFuelSignature, diagnostics, $"method '{methodName}' must charge fuel");
        RequireReturns(
            analysis,
            GeneratedMeterState.EnterCall | GeneratedMeterState.ExitCall | GeneratedMeterState.ChargeFuel,
            diagnostics,
            $"method '{methodName}' must meter every return path");
        if (analysis.HasUnmeteredCycle)
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must charge loop iterations in every control-flow cycle"));
        }

        VerifyMeterOrder(methodName, analysis, diagnostics);
        VerifyPositiveMeterAmounts(methodName, analysis, diagnostics);
        VerifyWorkHasMeterDensity(methodName, analysis, diagnostics);
        VerifyInstructionMeterDensity(methodName, analysis, diagnostics);
        VerifyRuntimeCallOrder(methodName, analysis, diagnostics);
        foreach (var instruction in analysis.Instructions.Where(i => i.IsLocalCall))
        {
            var state = analysis.EntryStates.TryGetValue(instruction.Offset, out var entryState)
                ? entryState
                : GeneratedMeterState.None;
            var required = GeneratedMeterState.EnterCall | GeneratedMeterState.ChargeFuel;
            if ((state & required) != required)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must enter call depth and charge fuel before local calls"));
            }

            if ((state & GeneratedMeterState.ExitCall) == GeneratedMeterState.ExitCall)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must not call generated functions after exiting the call meter"));
            }
        }
    }

    private static void VerifyRuntimeCallOrder(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var instruction in analysis.Instructions.Where(i => IsRuntimeWorkCall(i.CalledMember)))
        {
            var state = analysis.EntryStates.TryGetValue(instruction.Offset, out var entryState)
                ? entryState
                : GeneratedMeterState.None;
            var required = GeneratedMeterState.EnterCall | GeneratedMeterState.ChargeFuel;
            if ((state & required) != required)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must enter call depth and charge fuel before runtime work"));
            }

            if ((state & GeneratedMeterState.ExitCall) == GeneratedMeterState.ExitCall)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must not call runtime work after exiting the call meter"));
            }
        }
    }

    private static void VerifyMeterOrder(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var instruction in analysis.Instructions.Where(i => i.CalledMember == EnterCall || i.CalledMember == ExitCall))
        {
            if (!analysis.EntryStates.TryGetValue(instruction.Offset, out var state))
            {
                continue;
            }

            if (instruction.CalledMember == ExitCall && (state & GeneratedMeterState.EnterCall) == 0)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must not exit the call meter before entering it"));
            }

            if (instruction.CalledMember == EnterCall &&
                (state & GeneratedMeterState.EnterCall) != 0 &&
                (state & GeneratedMeterState.ExitCall) == 0)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must not enter the call meter twice without exiting"));
            }
        }
    }

    private static void VerifyPositiveMeterAmounts(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        for (var i = 0; i < analysis.Instructions.Count; i++)
        {
            var instruction = analysis.Instructions[i];
            if (!IsReachable(analysis, instruction) ||
                !IsFuelMeter(instruction.CalledMember))
            {
                continue;
            }

            if (GeneratedMethodMeterAnalyzer.HasPositiveImmediateMeterAmount(analysis, i))
            {
                continue;
            }

            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must charge a positive meter amount"));
        }
    }

    private static void VerifyWorkHasMeterDensity(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        if (GeneratedMethodMeterAnalyzer.HasUnmeteredWorkPath(analysis, IsFuelMeter, IsMeterDensityWorkCall))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must meter each runtime work call"));
        }
    }

    private static void VerifyInstructionMeterDensity(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        if (GeneratedMethodMeterAnalyzer.HasSparseMeterPath(analysis, IsFuelMeter))
        {
            diagnostics.Add(new VerificationDiagnostic(
                "V-COMPILED-SHAPE",
                $"method '{methodName}' must not execute long instruction sequences without fuel metering"));
        }
    }

    private static GeneratedMeterState StateFor(GeneratedInstruction instruction)
    {
        var state = instruction.CalledMember switch
        {
            var member when member == ValidateInput => GeneratedMeterState.ValidateInput,
            var member when member == EnterCall => GeneratedMeterState.EnterCall,
            var member when member == ExitCall => GeneratedMeterState.ExitCall,
            var member when member == ChargeFuelSignature => GeneratedMeterState.ChargeFuel,
            var member when member == ChargeLoopIterationSignature => GeneratedMeterState.ChargeFuel,
            _ => GeneratedMeterState.None
        };
        return instruction.IsLocalCall && IsGeneratedFunctionCall(instruction.CalledMember)
            ? state | GeneratedMeterState.LocalFunctionCall
            : state;
    }

    private static bool IsGeneratedFunctionCall(string? calledMember)
        => calledMember is not null && calledMember.StartsWith("Fn_", StringComparison.Ordinal);

    private static bool IsFuelMeter(string? calledMember)
        => calledMember == ChargeFuelSignature || calledMember == ChargeLoopIterationSignature;

    private static bool IsReachable(GeneratedMethodFlow analysis, GeneratedInstruction instruction)
        => analysis.EntryStates.ContainsKey(instruction.Offset);

    private static bool IsRuntimeWorkCall(string? calledMember)
        => calledMember is not null &&
           calledMember.StartsWith(Runtime + ".", StringComparison.Ordinal) &&
           calledMember != EnterCall &&
           calledMember != ExitCall &&
           calledMember != ChargeFuelSignature &&
           calledMember != ChargeLoopIterationSignature &&
           calledMember != RequireValueType;

    private static bool IsMeterDensityWorkCall(string? calledMember)
    {
        if (!IsRuntimeWorkCall(calledMember) || calledMember is null)
        {
            return false;
        }

        return !calledMember.StartsWith(Runtime + ".CallBinding(" + Context, StringComparison.Ordinal) &&
               !calledMember.StartsWith(Runtime + ".CreateValueArray(" + Context, StringComparison.Ordinal) &&
               !calledMember.Contains("(" + Context + ",", StringComparison.Ordinal);
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
