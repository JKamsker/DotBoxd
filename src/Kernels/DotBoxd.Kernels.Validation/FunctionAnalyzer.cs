namespace DotBoxd.Kernels.Validation;

using DotBoxd.Kernels;

internal sealed class FunctionAnalyzer
{
    private readonly IBindingCatalog _bindings;
    private readonly List<SandboxDiagnostic> _diagnostics;
    private readonly Dictionary<string, SandboxFunction> _functions;
    private readonly CollectionCallAnalyzer _collections;
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
    }

    public HashSet<string> RequiredCapabilities { get; } = new(StringComparer.Ordinal);

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
                AnalyzeDeadStatement(statement, scope, function.ReturnType);
                continue;
            }

            alwaysReturns = AnalyzeStatement(
                statement,
                scope,
                function.ReturnType,
                ref effects,
                ref canReorder,
                recordCapabilities: true);
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

    private bool AnalyzeStatement(
        Statement statement,
        FunctionScope scope,
        SandboxType returnType,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        switch (statement)
        {
            case AssignmentStatement assignment:
                var assignmentType = AnalyzeExpression(
                    assignment.Value,
                    scope,
                    ref effects,
                    ref canReorder,
                    recordCapabilities);
                scope.Set(assignment.Name, assignmentType, _diagnostics, assignment.Span);
                return false;
            case ReturnStatement ret:
                Require(
                    AnalyzeExpression(ret.Value, scope, ref effects, ref canReorder, recordCapabilities),
                    returnType,
                    ret.Span);
                return true;
            case ExpressionStatement expr:
                AnalyzeExpression(expr.Value, scope, ref effects, ref canReorder, recordCapabilities);
                return false;
            case IfStatement branch:
                Require(
                    AnalyzeExpression(branch.Condition, scope, ref effects, ref canReorder, recordCapabilities),
                    SandboxType.Bool,
                    branch.Span);
                var thenReturns = AnalyzeBlock(
                    branch.Then,
                    scope.Clone(),
                    returnType,
                    ref effects,
                    ref canReorder,
                    recordCapabilities);
                var elseReturns = AnalyzeBlock(
                    branch.Else,
                    scope.Clone(),
                    returnType,
                    ref effects,
                    ref canReorder,
                    recordCapabilities);
                return thenReturns && elseReturns;
            case WhileStatement loop:
                Require(
                    AnalyzeExpression(loop.Condition, scope, ref effects, ref canReorder, recordCapabilities),
                    SandboxType.Bool,
                    loop.Span);
                AnalyzeBlock(loop.Body, scope.Clone(), returnType, ref effects, ref canReorder, recordCapabilities);
                return false;
            case ForRangeStatement range:
                Require(
                    AnalyzeExpression(range.Start, scope, ref effects, ref canReorder, recordCapabilities),
                    SandboxType.I32,
                    range.Span);
                Require(
                    AnalyzeExpression(range.End, scope, ref effects, ref canReorder, recordCapabilities),
                    SandboxType.I32,
                    range.Span);
                var child = scope.Clone();
                child.Set(range.LocalName, SandboxType.I32, _diagnostics, range.Span);
                AnalyzeBlock(range.Body, child, returnType, ref effects, ref canReorder, recordCapabilities);
                return false;
            default:
                _diagnostics.Add(new SandboxDiagnostic("E-STMT-UNKNOWN", $"unsupported statement '{statement.GetType().Name}'", Span: statement.Span));
                return false;
        }
    }

    private void AnalyzeDeadStatement(Statement statement, FunctionScope scope, SandboxType returnType)
    {
        var ignoredEffects = SandboxEffect.None;
        var ignoredCanReorder = true;
        _ = AnalyzeStatement(
            statement,
            scope,
            returnType,
            ref ignoredEffects,
            ref ignoredCanReorder,
            recordCapabilities: false);
    }

    private bool AnalyzeBlock(
        IReadOnlyList<Statement> block,
        FunctionScope scope,
        SandboxType returnType,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        var alwaysReturns = false;
        foreach (var statement in block)
        {
            if (alwaysReturns)
            {
                AnalyzeDeadStatement(statement, scope, returnType);
                continue;
            }

            alwaysReturns = AnalyzeStatement(
                statement,
                scope,
                returnType,
                ref effects,
                ref canReorder,
                recordCapabilities);
        }

        return alwaysReturns;
    }

    private SandboxType AnalyzeExpression(
        Expression expression,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        effects |= SandboxEffect.Cpu;
        return expression switch
        {
            LiteralExpression literal => LiteralExpressionAnalyzer.Analyze(literal, ref effects),
            VariableExpression variable => scope.Get(variable.Name, _diagnostics, variable.Span),
            UnaryExpression unary => AnalyzeUnary(unary, scope, ref effects, ref canReorder, recordCapabilities),
            BinaryExpression binary => AnalyzeBinary(binary, scope, ref effects, ref canReorder, recordCapabilities),
            CallExpression call => AnalyzeCall(call, scope, ref effects, ref canReorder, recordCapabilities),
            _ => UnknownExpression(expression)
        };
    }

    private SandboxType UnknownExpression(Expression expression)
    {
        _diagnostics.Add(new SandboxDiagnostic("E-EXPR-UNKNOWN", $"unsupported expression '{expression.GetType().Name}'", Span: expression.Span));
        return SandboxType.Unit;
    }

    private SandboxType AnalyzeUnary(
        UnaryExpression unary,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        var operand = AnalyzeExpression(unary.Operand, scope, ref effects, ref canReorder, recordCapabilities);
        if (unary.Operator == "!")
        {
            Require(operand, SandboxType.Bool, unary.Span);
            return SandboxType.Bool;
        }

        if (unary.Operator != "-")
        {
            _diagnostics.Add(new SandboxDiagnostic("E-OP-UNKNOWN", $"unknown unary operator '{unary.Operator}'", Span: unary.Span));
            return SandboxType.Unit;
        }

        return AnalyzeNumericUnary(unary, operand);
    }

    private SandboxType AnalyzeBinary(
        BinaryExpression binary,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        var left = AnalyzeExpression(binary.Left, scope, ref effects, ref canReorder, recordCapabilities);
        var right = AnalyzeExpression(binary.Right, scope, ref effects, ref canReorder, recordCapabilities);
        if (binary.Operator is "==" or "!=")
        {
            Require(left, right, binary.Span);
            return SandboxType.Bool;
        }

        if (binary.Operator is "<" or "<=" or ">" or ">=")
        {
            return AnalyzeNumericBinary(binary, left, right, comparison: true);
        }

        if (binary.Operator is "&&" or "||")
        {
            Require(left, SandboxType.Bool, binary.Span);
            Require(right, SandboxType.Bool, binary.Span);
            return SandboxType.Bool;
        }

        if (binary.Operator is not ("+" or "-" or "*" or "/" or "%"))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-OP-UNKNOWN", $"unknown binary operator '{binary.Operator}'", Span: binary.Span));
            return SandboxType.Unit;
        }

        return AnalyzeNumericBinary(binary, left, right, comparison: false);
    }

    private SandboxType AnalyzeNumericUnary(UnaryExpression unary, SandboxType operand)
    {
        if (IsNumeric(operand))
        {
            return operand;
        }

        _diagnostics.Add(new SandboxDiagnostic(
            "E-TYPE-MISMATCH",
            $"expected numeric operand, got {operand}",
            Span: unary.Span));
        return SandboxType.Unit;
    }

    private SandboxType AnalyzeNumericBinary(
        BinaryExpression binary,
        SandboxType left,
        SandboxType right,
        bool comparison)
    {
        if (left == right && IsNumeric(left))
        {
            return comparison ? SandboxType.Bool : left;
        }

        _diagnostics.Add(new SandboxDiagnostic(
            "E-TYPE-MISMATCH",
            $"expected matching numeric operands, got {left} and {right}",
            Span: binary.Span));
        return comparison ? SandboxType.Bool : SandboxType.Unit;
    }

    private SandboxType AnalyzeCall(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        ValidateGenericType(call);
        if (_collections.TryAnalyze(
                call,
                scope,
                ref effects,
                ref canReorder,
                recordCapabilities,
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
                ref canReorder,
                recordCapabilities);
            var analysis = Analyze(function.Id);
            effects |= analysis.Effects;
            canReorder &= analysis.CanReorder;
            return function.ReturnType;
        }

        if (_bindings.TryGet(call.Name, out var binding))
        {
            CheckArguments(call, binding.Parameters, scope, ref effects, ref canReorder, recordCapabilities);
            effects |= binding.Effects;
            canReorder &= CanReorderBinding(binding);
            if (recordCapabilities && binding.RequiredCapability is not null)
            {
                RequiredCapabilities.Add(binding.RequiredCapability);
            }

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

        if (!call.GenericType.IsKnown(_declaredOpaqueIdTypes) || call.GenericType.IsForbidden())
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
        ref bool canReorder,
        bool recordCapabilities)
    {
        if (call.Arguments.Count != expected.Count)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-ARITY", $"call '{call.Name}' expects {expected.Count} arguments", Span: call.Span));
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            Require(
                AnalyzeExpression(call.Arguments[i], scope, ref effects, ref canReorder, recordCapabilities),
                expected[i].Type,
                call.Arguments[i].Span);
        }
    }

    private void CheckArguments(
        CallExpression call,
        IReadOnlyList<SandboxType> expected,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        bool recordCapabilities)
    {
        if (call.Arguments.Count != expected.Count)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-ARITY", $"call '{call.Name}' expects {expected.Count} arguments", Span: call.Span));
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            Require(
                AnalyzeExpression(call.Arguments[i], scope, ref effects, ref canReorder, recordCapabilities),
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
