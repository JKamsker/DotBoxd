namespace SafeIR.Validation;

using SafeIR;

internal static class StructuralValidator
{
    public static void Validate(SandboxModule module, List<SandboxDiagnostic> diagnostics)
    {
        CheckIdentifier(module.Id, "module id", diagnostics);

        foreach (var request in module.CapabilityRequests) {
            CheckIdentifier(request.Id, "capability id", diagnostics);
            CheckOptionalText(request.Reason, "capability reason", diagnostics);
        }

        foreach (var item in module.Metadata) {
            CheckIdentifier(item.Key, "metadata key", diagnostics);
            CheckIdentifier(item.Value, "metadata value", diagnostics);
        }

        foreach (var group in module.Functions.GroupBy(f => f.Id, StringComparer.Ordinal).Where(g => g.Count() > 1)) {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-FN", $"duplicate function id '{group.Key}'"));
        }

        if (!module.Functions.Any(f => f.IsEntrypoint)) {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-ENTRY", "module must declare at least one entry function"));
        }

        foreach (var function in module.Functions) {
            ValidateFunction(function, diagnostics);
        }
    }

    private static void ValidateFunction(SandboxFunction function, List<SandboxDiagnostic> diagnostics)
    {
        CheckIdentifier(function.Id, "function id", diagnostics);
        CheckType(function.ReturnType, diagnostics);
        foreach (var group in function.Parameters.GroupBy(p => p.Name, StringComparer.Ordinal).Where(g => g.Count() > 1)) {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-DUP-PARAM", $"duplicate parameter '{group.Key}' in function '{function.Id}'"));
        }

        foreach (var parameter in function.Parameters) {
            CheckIdentifier(parameter.Name, "parameter name", diagnostics);
            CheckType(parameter.Type, diagnostics);
        }

        foreach (var statement in function.Body) {
            DangerousReferenceDetector.Scan(statement, diagnostics);
        }
    }

    private static void CheckType(SandboxType type, List<SandboxDiagnostic> diagnostics)
    {
        if (!type.IsKnown() || type.IsForbidden()) {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{type}'"));
        }
    }

    private static void CheckIdentifier(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (DangerousReferenceDetector.IsDangerousReference(value)) {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"{description} '{value}' looks like a forbidden CLR reference"));
        }
    }

    private static void CheckOptionalText(string? value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (value is not null) {
            CheckIdentifier(value, description, diagnostics);
        }
    }
}
