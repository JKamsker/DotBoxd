using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation;

using DotBoxD.Kernels;

internal sealed partial class FunctionAnalyzer
{
    private readonly IBindingCatalog _bindings;
    private readonly List<SandboxDiagnostic> _diagnostics;
    private readonly Dictionary<string, SandboxFunction> _functions;
    private readonly CollectionCallAnalyzer _collections;
    private readonly NumericConversionCallAnalyzer _numericConversions;
    private readonly IReadOnlySet<string> _declaredOpaqueIdTypes;
    private readonly Dictionary<string, FunctionAnalysis> _analyzed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _analyzing = new(StringComparer.Ordinal);

    public FunctionAnalyzer(
        SandboxModule module,
        IBindingCatalog bindings,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        _bindings = bindings;
        _diagnostics = diagnostics;
        _declaredOpaqueIdTypes = declaredOpaqueIdTypes;
        _functions = new Dictionary<string, SandboxFunction>(module.Functions.Count, StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            _functions.Add(function.Id, function);
        }

        _collections = new CollectionCallAnalyzer(diagnostics, AnalyzeExpression, declaredOpaqueIdTypes);
        _numericConversions = new NumericConversionCallAnalyzer(diagnostics, AnalyzeExpression);
    }

    public IReadOnlyDictionary<string, FunctionAnalysis> AnalyzeAll()
    {
        foreach (var function in _functions.Values)
        {
            Analyze(function.Id);
        }

        return _analyzed;
    }

    private FunctionAnalysis Analyze(string functionId)
    {
        if (_analyzed.TryGetValue(functionId, out var existing))
        {
            return existing;
        }

        if (!_analyzing.Add(functionId))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-RECURSION", $"recursive call involving '{functionId}' is not allowed"));
            return new FunctionAnalysis(SandboxType.Unit, SandboxEffect.None, CanReorder: false);
        }

        var function = _functions[functionId];
        var scope = FunctionScope.FromParameters(function.Parameters);
        var effects = SandboxEffect.Cpu;
        var canReorder = true;
        var alwaysReturns = false;
        foreach (var statement in function.Body)
        {
            if (alwaysReturns)
            {
                AnalyzeDeadStatement(statement, scope, function.ReturnType, loopDepth: 0);
                continue;
            }

            alwaysReturns = AnalyzeStatement(
                statement,
                scope,
                function.ReturnType,
                ref effects,
                ref canReorder,
                loopDepth: 0);
        }

        if (!alwaysReturns)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-FN-RETURN", $"function '{function.Id}' is missing a guaranteed return"));
        }

        _analyzing.Remove(functionId);
        var finalEffects = effects | SandboxEffect.Cpu;
        var result = new FunctionAnalysis(function.ReturnType, finalEffects, canReorder && IsPure(finalEffects));
        _analyzed[functionId] = result;
        return result;
    }

    private SandboxType AnalyzeCall(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        ValidateGenericType(call);
        if (_numericConversions.TryAnalyze(
                call,
                scope,
                ref effects,
                ref canReorder,
                out var convertedType))
        {
            return convertedType;
        }

        if (_collections.TryAnalyze(
                call,
                scope,
                ref effects,
                ref canReorder,
                out var collectionType))
        {
            return collectionType;
        }

        if (_functions.TryGetValue(call.Name, out var function))
        {
            CheckArguments(
                call,
                function.Parameters,
                scope,
                ref effects,
                ref canReorder);
            var analysis = Analyze(function.Id);
            effects |= analysis.Effects;
            canReorder &= analysis.CanReorder;
            return function.ReturnType;
        }

        if (_bindings.TryGet(call.Name, out var binding))
        {
            CheckArguments(call, binding.Parameters, scope, ref effects, ref canReorder);
            effects |= binding.Effects;
            if (binding.IsAsync)
            {
                effects |= SandboxEffect.Concurrency;
            }

            canReorder &= CanReorderBinding(binding);
            return binding.ReturnType;
        }

        _diagnostics.Add(new SandboxDiagnostic("E-CALL-UNKNOWN", $"unknown function or binding '{call.Name}'", Span: call.Span));
        canReorder = false;
        return SandboxType.Unit;
    }

    private void ValidateGenericType(CallExpression call)
    {
        if (call.GenericType is null)
        {
            return;
        }

        if (!call.GenericType.IsKnown(_declaredOpaqueIdTypes))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{call.GenericType}'", Span: call.Span));
        }

        if (call.Name is not ("list.empty" or "map.empty" or "record.new"))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", $"call '{call.Name}' does not accept genericType", Span: call.Span));
        }
    }

    private void CheckArguments(
        CallExpression call,
        IReadOnlyList<Parameter> expected,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != expected.Count)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-ARITY", $"call '{call.Name}' expects {expected.Count} arguments", Span: call.Span));
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            Require(
                AnalyzeExpression(call.Arguments[i], scope, ref effects, ref canReorder),
                expected[i].Type,
                call.Arguments[i].Span);
        }
    }

    private void CheckArguments(
        CallExpression call,
        IReadOnlyList<SandboxType> expected,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != expected.Count)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-ARITY", $"call '{call.Name}' expects {expected.Count} arguments", Span: call.Span));
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            Require(
                AnalyzeExpression(call.Arguments[i], scope, ref effects, ref canReorder),
                expected[i],
                call.Arguments[i].Span);
        }
    }

    private static bool CanReorderBinding(BindingSignature binding)
        => binding.Safety == BindingSafety.PureIntrinsic && IsPure(binding.Effects);

    private static bool IsPure(SandboxEffect effects) => (effects & ~SandboxEffects.Pure) == SandboxEffect.None;

    private static bool IsNumeric(SandboxType type)
        => type == SandboxType.I32 || type == SandboxType.I64 || type == SandboxType.F64;

    private void Require(SandboxType actual, SandboxType expected, SourceSpan span)
    {
        if (actual != expected)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected {expected}, got {actual}", Span: span));
        }
    }
}
