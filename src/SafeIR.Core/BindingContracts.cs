namespace SafeIR;

public delegate ValueTask<SandboxValue> BindingInvoker(
    SandboxContext context,
    IReadOnlyList<SandboxValue> args,
    CancellationToken cancellationToken);

public delegate void CapabilityGrantValidator(
    CapabilityGrant grant,
    ICollection<SandboxDiagnostic> diagnostics);

public enum BindingSafety
{
    PureIntrinsic,
    PureHostFacade,
    ReadOnlyExternal,
    SideEffectingExternal,
    DangerousRequiresReview
}

public sealed record BindingCostModel(
    long BaseFuel,
    long PerByteFuel = 0,
    bool AllocationFromReturnBytes = false,
    int? MaxCallsPerRun = null)
{
    public static BindingCostModel Fixed(long baseFuel) => new(baseFuel);

    public static BindingCostModel PerByte(long baseFuel, long perByteFuel)
        => new(baseFuel, perByteFuel);

    public static BindingCostModel PerReturnedByte(long baseFuel, long perByteFuel)
        => new(baseFuel, perByteFuel, AllocationFromReturnBytes: true);
}

public sealed record CompiledBinding(string Kind, string Type, string Method)
{
    public static CompiledBinding RuntimeStub(string type, string method) => new("RuntimeStub", type, method);
}

public sealed record BindingSignature(
    string Id,
    SemVersion Version,
    IReadOnlyList<SandboxType> Parameters,
    SandboxType ReturnType,
    SandboxEffect Effects,
    string? RequiredCapability,
    BindingCostModel CostModel,
    AuditLevel AuditLevel,
    BindingSafety Safety,
    CompiledBinding Compiled);

public sealed record BindingDescriptor(
    string Id,
    SemVersion Version,
    IReadOnlyList<SandboxType> Parameters,
    SandboxType ReturnType,
    SandboxEffect Effects,
    string? RequiredCapability,
    BindingCostModel CostModel,
    AuditLevel AuditLevel,
    BindingSafety Safety,
    BindingInvoker Invoke,
    CompiledBinding Compiled,
    CapabilityGrantValidator? GrantValidator = null)
{
    public BindingSignature Signature => new(
        Id, Version, Parameters.ToArray(), ReturnType, Effects, RequiredCapability, CostModel, AuditLevel, Safety, Compiled);
}

public interface IBindingCatalog
{
    bool TryGet(string id, out BindingSignature binding);
    bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator);
    IReadOnlyList<BindingSignature> Signatures { get; }
    string ManifestHash { get; }
}

public sealed class BindingRegistry : IBindingCatalog
{
    private readonly Dictionary<string, BindingDescriptor> _bindings;
    private readonly Dictionary<string, CapabilityGrantValidator> _grantValidators;

    public BindingRegistry(IEnumerable<BindingDescriptor> bindings)
    {
        var frozen = bindings.Select(Freeze).ToArray();
        _bindings = frozen.ToDictionary(b => b.Id, StringComparer.Ordinal);
        _grantValidators = frozen
            .Where(b => !string.IsNullOrWhiteSpace(b.RequiredCapability) && b.GrantValidator is not null)
            .GroupBy(b => b.RequiredCapability!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().GrantValidator!, StringComparer.Ordinal);
        ManifestHash = ComputeManifestHash(Signatures);
    }

    public IReadOnlyList<BindingSignature> Signatures => _bindings.Values.Select(b => b.Signature).OrderBy(b => b.Id).ToArray();

    public string ManifestHash { get; }

    public BindingDescriptor GetDescriptor(string id) => _bindings[id];

    public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
    {
        if (_grantValidators.TryGetValue(capabilityId, out var found)) {
            validator = found;
            return true;
        }

        validator = default!;
        return false;
    }

    public bool TryGet(string id, out BindingSignature binding)
    {
        if (_bindings.TryGetValue(id, out var descriptor)) {
            binding = descriptor.Signature;
            return true;
        }

        binding = default!;
        return false;
    }

    private static string ComputeManifestHash(IEnumerable<BindingSignature> signatures)
    {
        var records = new List<string> {
            CanonicalEncoding.Record("bindings-v2")
        };
        records.AddRange(signatures.Select(BindingRecord).Order(StringComparer.Ordinal));
        return CanonicalEncoding.HashRecords(records);
    }

    private static BindingDescriptor Freeze(BindingDescriptor binding)
        => binding with { Parameters = binding.Parameters.ToArray() };

    private static string BindingRecord(BindingSignature binding)
    {
        var fields = new List<string?> {
            "binding",
            binding.Id,
            binding.Version.ToString(),
            Type(binding.ReturnType),
            ((long)binding.Effects).ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.RequiredCapability,
            binding.CostModel.BaseFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CostModel.PerByteFuel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CostModel.AllocationFromReturnBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.CostModel.MaxCallsPerRun?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding.AuditLevel.ToString(),
            binding.Safety.ToString(),
            binding.Compiled.Kind,
            binding.Compiled.Type,
            binding.Compiled.Method
        };
        fields.AddRange(binding.Parameters.Select(Type));
        return CanonicalEncoding.Record(fields);
    }

    private static string Type(SandboxType type)
    {
        var fields = new List<string?> { "type", type.Name };
        fields.AddRange(type.Arguments.Select(Type));
        return CanonicalEncoding.Record(fields);
    }
}

public sealed class BindingRegistryBuilder
{
    private readonly List<BindingDescriptor> _bindings = [];

    public BindingRegistryBuilder Add(BindingDescriptor descriptor)
    {
        _bindings.Add(descriptor);
        return this;
    }

    public BindingRegistryBuilder AddRange(IEnumerable<BindingDescriptor> descriptors)
    {
        _bindings.AddRange(descriptors);
        return this;
    }

    public BindingRegistry Build()
    {
        var diagnostics = BindingRegistryValidator.Validate(_bindings);
        if (diagnostics.Count > 0) {
            throw new SandboxValidationException(diagnostics);
        }

        return new BindingRegistry(_bindings);
    }
}

internal static class BindingRegistryValidator
{
    private const string RuntimeStubKind = "RuntimeStub";
    private const string ApprovedCompiledRuntimeType = "SafeIR.Runtime.CompiledRuntime";
    private const string GenericBindingStub = "CallBinding";

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

    public static IReadOnlyList<SandboxDiagnostic> Validate(IReadOnlyList<BindingDescriptor> bindings)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        foreach (var group in bindings.GroupBy(b => b.Id, StringComparer.Ordinal).Where(g => g.Count() > 1)) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-DUP", $"duplicate binding id '{group.Key}'"));
        }

        foreach (var binding in bindings) {
            ValidateBinding(binding, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateBinding(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        if (!binding.Effects.ContainsOnlyKnownBits()) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-EFFECT", $"binding '{binding.Id}' declares an unknown effect"));
        }

        if (binding.Effects.RequiresCapability() && string.IsNullOrWhiteSpace(binding.RequiredCapability)) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-CAP", $"binding '{binding.Id}' has side effects but no capability"));
        }

        if (binding.Safety == BindingSafety.DangerousRequiresReview) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-DANGER", $"binding '{binding.Id}' is dangerous and cannot be enabled by default"));
        }

        ValidateCostModel(binding, diagnostics);
        ValidateCompiledTarget(binding, diagnostics);
        foreach (var type in binding.Parameters.Append(binding.ReturnType)) {
            if (!type.IsKnown() || type.IsForbidden()) {
                diagnostics.Add(new SandboxDiagnostic("E-BINDING-TYPE", $"binding '{binding.Id}' exposes forbidden or unknown type '{type}'"));
            }
        }
    }

    private static void ValidateCostModel(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        var cost = binding.CostModel;
        if (cost.BaseFuel < 0 || cost.PerByteFuel < 0 || cost.MaxCallsPerRun is < 0) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COST", $"binding '{binding.Id}' declares a negative resource cost or call limit"));
        }
    }

    private static void ValidateCompiledTarget(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Compiled.Kind != RuntimeStubKind) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' has unsupported compiled target kind"));
        }

        if (string.IsNullOrWhiteSpace(binding.Compiled.Type) ||
            string.IsNullOrWhiteSpace(binding.Compiled.Method)) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' has an incomplete compiled target"));
            return;
        }

        if (binding.Compiled.Type != ApprovedCompiledRuntimeType ||
            !ApprovedCompiledRuntimeMethods.Contains(binding.Compiled.Method)) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' points compiled code outside the approved runtime stub surface"));
            return;
        }

        if (binding.Compiled.Method != GenericBindingStub && binding.Safety != BindingSafety.PureIntrinsic) {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{binding.Id}' uses a direct compiled runtime method but is not a pure intrinsic"));
        }
    }
}
