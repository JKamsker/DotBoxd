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
    CompiledBinding Compiled)
{
    private IReadOnlyList<SandboxType> _parameters = ModelCopy.List(Parameters);

    public IReadOnlyList<SandboxType> Parameters { get => _parameters; init => _parameters = ModelCopy.List(value); }
}

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
    private IReadOnlyList<SandboxType> _parameters = ModelCopy.List(Parameters);

    public IReadOnlyList<SandboxType> Parameters { get => _parameters; init => _parameters = ModelCopy.List(value); }

    public BindingSignature Signature => new(
        Id, Version, CopyParameters(Parameters), ReturnType, Effects, RequiredCapability, CostModel, AuditLevel, Safety, Compiled);

    private static SandboxType[] CopyParameters(IReadOnlyList<SandboxType> parameters)
    {
        if (parameters.Count == 0)
        {
            return Array.Empty<SandboxType>();
        }

        var copy = new SandboxType[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            copy[i] = parameters[i];
        }

        return copy;
    }
}

public interface IBindingCatalog
{
    bool TryGet(string id, out BindingSignature binding);
    bool Contains(string id);
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
        var frozen = FreezeAll(bindings);
        var diagnostics = BindingRegistryValidator.Validate(frozen);
        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }

        _bindings = CreateBindingDictionary(frozen);
        _grantValidators = CreateGrantValidators(frozen);
        ManifestHash = ComputeManifestHash(Signatures);
    }

    public IReadOnlyList<BindingSignature> Signatures
    {
        get
        {
            var signatures = new BindingSignature[_bindings.Count];
            var index = 0;
            foreach (var binding in _bindings.Values)
            {
                signatures[index++] = binding.Signature;
            }

            Array.Sort(signatures, static (left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
            return signatures;
        }
    }

    public string ManifestHash { get; }

    public BindingDescriptor GetDescriptor(string id) => _bindings[id];

    public bool Contains(string id) => _bindings.ContainsKey(id);

    public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
    {
        if (_grantValidators.TryGetValue(capabilityId, out var found))
        {
            validator = found;
            return true;
        }

        validator = default!;
        return false;
    }

    public bool TryGet(string id, out BindingSignature binding)
    {
        if (_bindings.TryGetValue(id, out var descriptor))
        {
            binding = descriptor.Signature;
            return true;
        }

        binding = default!;
        return false;
    }

    private static BindingDescriptor[] FreezeAll(IEnumerable<BindingDescriptor> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings is IReadOnlyCollection<BindingDescriptor> collection)
        {
            var frozen = new BindingDescriptor[collection.Count];
            var index = 0;
            foreach (var binding in bindings)
            {
                frozen[index++] = Freeze(binding);
            }

            return frozen;
        }

        var list = new List<BindingDescriptor>();
        foreach (var binding in bindings)
        {
            list.Add(Freeze(binding));
        }

        return list.ToArray();
    }

    private static Dictionary<string, BindingDescriptor> CreateBindingDictionary(IReadOnlyList<BindingDescriptor> bindings)
    {
        var dictionary = new Dictionary<string, BindingDescriptor>(bindings.Count, StringComparer.Ordinal);
        for (var i = 0; i < bindings.Count; i++)
        {
            dictionary.Add(bindings[i].Id, bindings[i]);
        }

        return dictionary;
    }

    private static Dictionary<string, CapabilityGrantValidator> CreateGrantValidators(IReadOnlyList<BindingDescriptor> bindings)
    {
        var grouped = new Dictionary<string, List<CapabilityGrantValidator>>(StringComparer.Ordinal);
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (string.IsNullOrWhiteSpace(binding.RequiredCapability) || binding.GrantValidator is null)
            {
                continue;
            }

            if (!grouped.TryGetValue(binding.RequiredCapability, out var validators))
            {
                validators = [];
                grouped.Add(binding.RequiredCapability, validators);
            }

            validators.Add(binding.GrantValidator);
        }

        var result = new Dictionary<string, CapabilityGrantValidator>(grouped.Count, StringComparer.Ordinal);
        foreach (var item in grouped)
        {
            var validators = item.Value;
            result.Add(
                item.Key,
                validators.Count == 1 ? validators[0] : ComposeGrantValidators(validators));
        }

        return result;
    }

    private static string ComputeManifestHash(IReadOnlyList<BindingSignature> signatures)
    {
        var records = new List<string>(signatures.Count + 1) {
            CanonicalEncoding.Record("bindings-v2")
        };
        for (var i = 0; i < signatures.Count; i++)
        {
            records.Add(BindingRecord(signatures[i]));
        }

        if (records.Count > 2)
        {
            records.Sort(1, records.Count - 1, StringComparer.Ordinal);
        }

        return CanonicalEncoding.HashRecords(records);
    }

    private static BindingDescriptor Freeze(BindingDescriptor binding)
        => binding with { Parameters = CopyParameters(binding.Parameters) };

    private static SandboxType[] CopyParameters(IReadOnlyList<SandboxType> parameters)
    {
        if (parameters.Count == 0)
        {
            return Array.Empty<SandboxType>();
        }

        var copy = new SandboxType[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            copy[i] = parameters[i];
        }

        return copy;
    }

    private static CapabilityGrantValidator ComposeGrantValidators(IReadOnlyList<CapabilityGrantValidator> validators)
        => (grant, diagnostics) =>
        {
            foreach (var validator in validators)
            {
                validator(grant, diagnostics);
            }
        };

    private static string BindingRecord(BindingSignature binding)
    {
        var fields = new List<string?>(15 + binding.Parameters.Count) {
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
        for (var i = 0; i < binding.Parameters.Count; i++)
        {
            fields.Add(Type(binding.Parameters[i]));
        }

        return CanonicalEncoding.Record(fields);
    }

    private static string Type(SandboxType type)
    {
        var fields = new List<string?>(2 + type.Arguments.Count) { "type", type.Name };
        for (var i = 0; i < type.Arguments.Count; i++)
        {
            fields.Add(Type(type.Arguments[i]));
        }

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
        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }

        return new BindingRegistry(_bindings);
    }
}

