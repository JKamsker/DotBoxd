using System.Reflection.Metadata;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Verifier.Generated.Methods;

using static GeneratedMethodShapeSignatures;

internal static class GeneratedMethodShapeVerifier
{
    public static void VerifyBody(
        MetadataReader reader,
        MethodDefinition method,
        MethodBodyBlock body,
        MethodSignature<string> signature,
        IReadOnlyList<GeneratedInstruction> instructions,
        string methodName,
        List<VerificationDiagnostic> diagnostics)
    {
        VerifyLocalInitialization(body, methodName, diagnostics);
        var analysis = GeneratedMethodFlowAnalyzer.Analyze(instructions, StateFor);
        GeneratedStackVerifier.Verify(signature, analysis, diagnostics);
        GeneratedStackTypeVerifier.Verify(reader, method, body, signature, analysis, diagnostics);
        VerifySandboxTypeScalarShape(methodName, analysis, diagnostics);
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

    private static void VerifyLocalInitialization(
        MethodBodyBlock body,
        string methodName,
        List<VerificationDiagnostic> diagnostics)
    {
        if (body.LocalVariablesInitialized || body.LocalSignature.IsNil || !IsGeneratedExecutableMethod(methodName))
        {
            return;
        }

        diagnostics.Add(new VerificationDiagnostic(
            "V-COMPILED-SHAPE",
            $"method '{methodName}' declares locals with initlocals disabled; generated method bodies must use local initialization"));
    }

    private static bool IsGeneratedExecutableMethod(string methodName)
        => methodName == "Execute" || methodName.StartsWith("Fn_", StringComparison.Ordinal);

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
        GeneratedUnchargedLiteralShapeVerifier.Verify(methodName, analysis, diagnostics);
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

    private static void VerifySandboxTypeScalarShape(
        string methodName,
        GeneratedMethodFlow analysis,
        List<VerificationDiagnostic> diagnostics)
    {
        foreach (var instruction in analysis.Instructions.Where(i => i.CalledMember == TypeScalarSignature))
        {
            if (!IsReachable(analysis, instruction))
            {
                continue;
            }

            var scalarName = PreviousStringLiteral(analysis, instruction);
            if (scalarName is null)
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' must construct SandboxType scalars from literal names"));
                continue;
            }

            if (!SandboxType.Scalar(scalarName).IsKnown())
            {
                diagnostics.Add(new VerificationDiagnostic(
                    "V-COMPILED-SHAPE",
                    $"method '{methodName}' constructs unknown or forbidden SandboxType scalar '{scalarName}'"));
            }
        }
    }

    private static string? PreviousStringLiteral(GeneratedMethodFlow analysis, GeneratedInstruction instruction)
    {
        if (!analysis.IndexByOffset.TryGetValue(instruction.Offset, out var index) || index == 0)
        {
            return null;
        }

        var previous = analysis.Instructions[index - 1];
        return previous.Opcode == ILOpCode.Ldstr ? previous.StringValue : null;
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

    private static bool IsReachable(GeneratedMethodFlow analysis, GeneratedInstruction instruction)
        => analysis.EntryStates.ContainsKey(instruction.Offset);

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
