namespace SafeIR;

internal static class BindingRegistryValidator
{
    private const string RuntimeStubKind = "RuntimeStub";
    private const string ApprovedCompiledRuntimeType = "SafeIR.Runtime.CompiledRuntime";
    private const string GenericBindingStub = "CallBinding";
    private static readonly string[] ForbiddenReferenceFragments = [
        "System.", "Microsoft.", "Assembly.", "Type.", "Reflection.", "Process.",
        "Environment.", "Thread.", "Task.", "DllImport", "IServiceProvider"
    ];

    private static readonly HashSet<string> ApprovedCompiledRuntimeMethods = new(StringComparer.Ordinal) {
        GenericBindingStub,
        "StringLength",
        "ConcatString",
        "AbsI32",
        "MinI32",
        "MaxI32",
        "ClampI32",
        "SqrtF64",
        "FloorF64",
        "CeilF64",
        "RoundF64"
    };

    private static readonly HashSet<string> BuiltInCapabilities = new(StringComparer.Ordinal) {
        "file.read", "file.write", "time.now", "random", "log.write"
    };

    private static readonly IReadOnlyDictionary<string, SandboxEffect> BuiltInCapabilityEffects =
        new Dictionary<string, SandboxEffect>(StringComparer.Ordinal) {
            ["file.read"] = SandboxEffect.FileRead,
            ["file.write"] = SandboxEffect.FileWrite,
            ["time.now"] = SandboxEffect.Time,
            ["random"] = SandboxEffect.Random,
            ["log.write"] = SandboxEffect.Audit
        };

    public static IReadOnlyList<SandboxDiagnostic> Validate(IReadOnlyList<BindingDescriptor> bindings)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        CheckDuplicateBindingIds(bindings, diagnostics);

        foreach (var binding in bindings)
        {
            ValidateBinding(binding, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateBinding(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        ValidateIdentifier(binding.Id, "binding id", "E-BINDING-ID", diagnostics);
        if (binding.RequiredCapability is not null)
        {
            ValidateIdentifier(binding.RequiredCapability, "required capability", "E-BINDING-CAP", diagnostics);
        }

        if (!binding.Effects.ContainsOnlyKnownBits())
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares an unknown effect"));
        }

        if (binding.Effects == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares no effects"));
        }

        if (binding.Effects.RequiresCapability() && string.IsNullOrWhiteSpace(binding.RequiredCapability))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' has side effects but no capability"));
        }

        if (!string.IsNullOrWhiteSpace(binding.RequiredCapability) &&
            (binding.Effects & ~SandboxEffect.Cpu) == SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-EFFECT",
                $"binding '{binding.Id}' requires a capability but declares only pure CPU effects"));
        }

        if (ReachesOutsideSandbox(binding))
        {
            if (string.IsNullOrWhiteSpace(binding.RequiredCapability))
            {
                diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' reaches outside the sandbox but has no capability"));
            }

            if (binding.AuditLevel == AuditLevel.None)
            {
                diagnostics.Add(new SandboxDiagnostic("E-BINDING-AUDIT", $"binding '{binding.Id}' reaches outside the sandbox but is not audited"));
            }
        }

        if (!string.IsNullOrWhiteSpace(binding.RequiredCapability) &&
            !BuiltInCapabilities.Contains(binding.RequiredCapability) &&
            binding.GrantValidator is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-GRANT",
                $"binding '{binding.Id}' uses custom capability '{binding.RequiredCapability}' without a grant validator"));
        }

        ValidateBuiltInCapabilityEffect(binding, diagnostics);

        if (binding.Safety == BindingSafety.DangerousRequiresReview)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-DANGER", $"binding '{binding.Id}' is dangerous and cannot be enabled by default"));
        }

        ValidateCostModel(binding, diagnostics);
        ValidateCompiledTarget(binding, diagnostics);
        foreach (var type in binding.Parameters)
        {
            ValidateType(binding, type, diagnostics);
        }

        ValidateType(binding, binding.ReturnType, diagnostics);
    }

    private static void CheckDuplicateBindingIds(
        IReadOnlyList<BindingDescriptor> bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        if (bindings.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(bindings.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < bindings.Count; i++)
        {
            IncrementCount(counts, bindings[i].Id, ref nullCount);
        }

        var reportedNull = false;
        for (var i = 0; i < bindings.Count; i++)
        {
            var id = bindings[i].Id;
            if (ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-BINDING-DUP", $"duplicate binding id '{id}'"));
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

    private static void ValidateType(
        BindingDescriptor binding,
        SandboxType type,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!type.IsKnownBuiltIn() || type.IsForbidden())
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-TYPE", $"binding '{binding.Id}' exposes forbidden or unknown type '{type}'"));
        }
    }

    private static void ValidateCostModel(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        var cost = binding.CostModel;
        if (cost.BaseFuel < 0 || cost.PerByteFuel < 0 || cost.MaxCallsPerRun is < 0)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COST", $"binding '{binding.Id}' declares a negative resource cost or call limit"));
        }
    }

    private static void ValidateIdentifier(
        string value,
        string description,
        string code,
        List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsControlCharacter(value))
        {
            diagnostics.Add(new SandboxDiagnostic(
                code,
                $"{description} must be non-empty and must not contain control characters"));
            return;
        }

        if (ContainsForbiddenReferenceFragment(value))
        {
            diagnostics.Add(new SandboxDiagnostic(code, $"{description} '{value}' looks like a forbidden CLR reference"));
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForbiddenReferenceFragment(string value)
    {
        for (var i = 0; i < ForbiddenReferenceFragments.Length; i++)
        {
            if (value.Contains(ForbiddenReferenceFragments[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReachesOutsideSandbox(BindingDescriptor binding)
        => IsExternal(binding.Safety) || binding.Effects.RequiresCapability();

    private static bool IsExternal(BindingSafety safety)
        => safety is BindingSafety.ReadOnlyExternal or BindingSafety.SideEffectingExternal;

    private static void ValidateBuiltInCapabilityEffect(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.RequiredCapability is null ||
            !BuiltInCapabilityEffects.TryGetValue(binding.RequiredCapability, out var requiredEffect))
        {
            return;
        }

        var allowedEffects = SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.Audit | requiredEffect;
        if ((binding.Effects & requiredEffect) == SandboxEffect.None ||
            (binding.Effects & ~allowedEffects) != SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-CAP-EFFECT",
                $"binding '{binding.Id}' uses built-in capability '{binding.RequiredCapability}' with incompatible effects"));
        }
    }

    private static void ValidateCompiledTarget(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Compiled.Kind != RuntimeStubKind)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' has unsupported compiled target kind"));
        }

        if (string.IsNullOrWhiteSpace(binding.Compiled.Type) ||
            string.IsNullOrWhiteSpace(binding.Compiled.Method))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' has an incomplete compiled target"));
            return;
        }

        if (binding.Compiled.Type != ApprovedCompiledRuntimeType ||
            !ApprovedCompiledRuntimeMethods.Contains(binding.Compiled.Method))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' points compiled code outside the approved runtime stub surface"));
            return;
        }

        if (binding.Compiled.Method != GenericBindingStub && binding.Safety != BindingSafety.PureIntrinsic)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' uses a direct compiled runtime method but is not a pure intrinsic"));
        }
    }
}
