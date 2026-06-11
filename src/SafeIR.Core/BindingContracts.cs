using System.Security.Cryptography;
using System.Text;

namespace SafeIR;

public delegate ValueTask<SandboxValue> InterpreterBinding(
    SandboxContext context,
    IReadOnlyList<SandboxValue> args,
    CancellationToken cancellationToken);

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
    InterpreterBinding Interpreter,
    CompiledBinding Compiled)
{
    public BindingSignature Signature => new(
        Id, Version, Parameters, ReturnType, Effects, RequiredCapability, CostModel, AuditLevel, Safety, Compiled);
}

public interface IBindingCatalog
{
    bool TryGet(string id, out BindingSignature binding);
    IReadOnlyList<BindingSignature> Signatures { get; }
    string ManifestHash { get; }
}

public sealed class BindingRegistry : IBindingCatalog
{
    private readonly Dictionary<string, BindingDescriptor> _bindings;

    public BindingRegistry(IEnumerable<BindingDescriptor> bindings)
    {
        _bindings = bindings.ToDictionary(b => b.Id, StringComparer.Ordinal);
        ManifestHash = ComputeManifestHash(Signatures);
    }

    public IReadOnlyList<BindingSignature> Signatures => _bindings.Values.Select(b => b.Signature).OrderBy(b => b.Id).ToArray();

    public string ManifestHash { get; }

    public BindingDescriptor GetDescriptor(string id) => _bindings[id];

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
        var builder = new StringBuilder("bindings-v1");
        foreach (var binding in signatures.OrderBy(b => b.Id, StringComparer.Ordinal)) {
            builder.Append('|').Append(binding.Id).Append('@').Append(binding.Version);
            builder.Append('|').Append(string.Join(",", binding.Parameters.Select(p => p.ToString())));
            builder.Append("->").Append(binding.ReturnType);
            builder.Append('|').Append((int)binding.Effects).Append('|').Append(binding.RequiredCapability);
            builder.Append('|').Append(binding.CostModel).Append('|').Append(binding.AuditLevel).Append('|').Append(binding.Safety);
            builder.Append('|').Append(binding.Compiled.Kind).Append(':').Append(binding.Compiled.Type).Append('.').Append(binding.Compiled.Method);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
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
