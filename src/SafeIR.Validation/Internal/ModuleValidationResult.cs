namespace SafeIR.Validation.Internal;

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using SafeIR;

public sealed record ModuleValidationResult(
    bool Succeeded,
    IReadOnlyList<SandboxDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, FunctionAnalysis> Functions,
    SandboxEffect ModuleEffects,
    IReadOnlySet<string> RequiredCapabilities,
    IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences)
{
    private IReadOnlyList<SandboxDiagnostic> _diagnostics = CopyList(Diagnostics);
    private IReadOnlyDictionary<string, FunctionAnalysis> _functions = CopyDictionary(Functions);
    private IReadOnlySet<string> _requiredCapabilities = CopySet(RequiredCapabilities);
    private IReadOnlyDictionary<string, IReadOnlySet<string>> _bindingReferences =
        CopyBindingReferences(BindingReferences);

    public IReadOnlyList<SandboxDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = CopyList(value);
    }

    public IReadOnlyDictionary<string, FunctionAnalysis> Functions
    {
        get => _functions;
        init => _functions = CopyDictionary(value);
    }

    public IReadOnlySet<string> RequiredCapabilities
    {
        get => _requiredCapabilities;
        init => _requiredCapabilities = CopySet(value);
    }

    public IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences
    {
        get => _bindingReferences;
        init => _bindingReferences = CopyBindingReferences(value);
    }

    public static ModuleValidationResult Failure(IReadOnlyList<SandboxDiagnostic> diagnostics)
        => new(
            false,
            diagnostics,
            new Dictionary<string, FunctionAnalysis>(),
            SandboxEffect.None,
            new HashSet<string>(),
            new Dictionary<string, IReadOnlySet<string>>());

    private static IReadOnlyList<T> CopyList<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }

    private static IReadOnlyDictionary<string, TValue> CopyDictionary<TValue>(
        IReadOnlyDictionary<string, TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<string, TValue>(
            new Dictionary<string, TValue>(values, StringComparer.Ordinal));
    }

    private static IReadOnlySet<string> CopySet(IReadOnlySet<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToFrozenSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<string, IReadOnlySet<string>>(values.Count, StringComparer.Ordinal);
        foreach (var item in values)
        {
            copy.Add(item.Key, CopySet(item.Value));
        }

        return new ReadOnlyDictionary<string, IReadOnlySet<string>>(copy);
    }
}
