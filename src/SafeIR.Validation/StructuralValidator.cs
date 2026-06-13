namespace SafeIR.Validation;

using SafeIR;

internal static class StructuralValidator
{
    public static void Validate(
        SandboxModule module,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        CheckIdentifier(module.Id, "module id", diagnostics);
        if (!SandboxLanguage.Supports(module.TargetSandboxVersion))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-IR-VERSION",
                $"target sandbox version '{module.TargetSandboxVersion}' is not supported by runtime '{SandboxLanguage.CurrentVersionText}'"));
        }

        foreach (var request in module.CapabilityRequests)
        {
            CheckIdentifier(request.Id, "capability id", diagnostics);
            CheckOptionalText(request.Reason, "capability reason", diagnostics);
        }

        CheckDuplicateCapabilityRequests(module.CapabilityRequests, diagnostics);

        foreach (var item in module.Metadata)
        {
            CheckIdentifier(item.Key, "metadata key", diagnostics);
            CheckText(item.Value, "metadata value", diagnostics);
        }

        CheckDuplicateFunctionIds(module.Functions, diagnostics);

        if (!HasEntrypoint(module.Functions))
        {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-ENTRY", "module must declare at least one entry function"));
        }

        foreach (var function in module.Functions)
        {
            ValidateFunction(function, diagnostics, declaredOpaqueIdTypes);
        }
    }

    private static void ValidateFunction(
        SandboxFunction function,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        CheckIdentifier(function.Id, "function id", diagnostics);
        CheckType(function.ReturnType, diagnostics, declaredOpaqueIdTypes);
        CheckDuplicateParameters(function, diagnostics);

        foreach (var parameter in function.Parameters)
        {
            CheckIdentifier(parameter.Name, "parameter name", diagnostics);
            CheckType(parameter.Type, diagnostics, declaredOpaqueIdTypes);
        }

        foreach (var statement in function.Body)
        {
            DangerousReferenceDetector.Scan(statement, diagnostics);
        }
    }

    private static void CheckDuplicateCapabilityRequests(
        IReadOnlyList<CapabilityRequest> requests,
        List<SandboxDiagnostic> diagnostics)
    {
        if (requests.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(requests.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < requests.Count; i++)
        {
            IncrementCount(counts, requests[i].Id, ref nullCount);
        }

        var reportedNull = false;
        for (var i = 0; i < requests.Count; i++)
        {
            var id = requests[i].Id;
            if (ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-CAP", $"duplicate capability request '{id}'"));
            }
        }
    }

    private static void CheckDuplicateFunctionIds(
        IReadOnlyList<SandboxFunction> functions,
        List<SandboxDiagnostic> diagnostics)
    {
        if (functions.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(functions.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < functions.Count; i++)
        {
            IncrementCount(counts, functions[i].Id, ref nullCount);
        }

        var reportedNull = false;
        for (var i = 0; i < functions.Count; i++)
        {
            var id = functions[i].Id;
            if (ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-FN", $"duplicate function id '{id}'"));
            }
        }
    }

    private static void CheckDuplicateParameters(
        SandboxFunction function,
        List<SandboxDiagnostic> diagnostics)
    {
        if (function.Parameters.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(function.Parameters.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            IncrementCount(counts, function.Parameters[i].Name, ref nullCount);
        }

        var reportedNull = false;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var name = function.Parameters[i].Name;
            if (ShouldReportDuplicate(counts, name, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-STRUCT-DUP-PARAM",
                    $"duplicate parameter '{name}' in function '{function.Id}'"));
            }
        }
    }

    private static void IncrementCount(Dictionary<string, int> counts, string? value, ref int nullCount)
    {
        if (value is null)
        {
            nullCount++;
            return;
        }

        counts.TryGetValue(value, out var count);
        counts[value] = count + 1;
    }

    private static bool ShouldReportDuplicate(
        Dictionary<string, int> counts,
        string? value,
        int nullCount,
        ref bool reportedNull)
    {
        if (value is null)
        {
            if (nullCount < 2 || reportedNull)
            {
                return false;
            }

            reportedNull = true;
            return true;
        }

        if (!counts.TryGetValue(value, out var count) || count < 2)
        {
            return false;
        }

        counts[value] = 0;
        return true;
    }

    private static bool HasEntrypoint(IReadOnlyList<SandboxFunction> functions)
    {
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i].IsEntrypoint)
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckType(
        SandboxType type,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        if (type.Name == "Map" && type.Arguments.Count == 2 && !type.Arguments[0].IsValidMapKey(declaredOpaqueIdTypes))
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-MAP-KEY", $"map key type '{type.Arguments[0]}' is not supported"));
        }

        if (!type.IsKnown(declaredOpaqueIdTypes) || type.IsForbidden())
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{type}'"));
        }
    }

    private static void CheckIdentifier(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-ID", $"{description} must be non-empty and must not contain control characters"));
            return;
        }

        if (DangerousReferenceDetector.IsDangerousReference(value))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"{description} '{value}' looks like a forbidden CLR reference"));
        }
    }

    private static void CheckOptionalText(string? value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (value is not null)
        {
            CheckText(value, description, diagnostics);
        }
    }

    private static void CheckText(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (DangerousReferenceDetector.IsDangerousReference(value))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"{description} '{value}' looks like a forbidden CLR reference"));
        }
    }
}
