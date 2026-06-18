using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal delegate SandboxType ExpressionAnalyzer(
    Expression expression,
    FunctionScope scope,
    ref SandboxEffect effects,
    ref bool canReorder);

internal sealed partial class CollectionCallAnalyzer
{
    private readonly List<SandboxDiagnostic> _diagnostics;
    private readonly ExpressionAnalyzer _analyzeExpression;
    private readonly IReadOnlySet<string> _declaredOpaqueIdTypes;

    public CollectionCallAnalyzer(List<SandboxDiagnostic> diagnostics, ExpressionAnalyzer analyzeExpression, IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        _diagnostics = diagnostics;
        _analyzeExpression = analyzeExpression;
        _declaredOpaqueIdTypes = declaredOpaqueIdTypes;
    }

    public bool TryAnalyze(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        out SandboxType type)
    {
        if (!IsCollectionCall(call.Name))
        {
            type = SandboxType.Unit;
            return false;
        }

        type = call.Name switch
        {
            "list.empty" => AnalyzeListEmpty(call, ref effects),
            "list.of" => AnalyzeListOf(call, scope, ref effects, ref canReorder),
            "list.count" => AnalyzeListCount(call, scope, ref effects, ref canReorder),
            "list.get" => AnalyzeListGet(call, scope, ref effects, ref canReorder),
            "list.add" => AnalyzeListAdd(call, scope, ref effects, ref canReorder),
            "map.empty" => AnalyzeMapEmpty(call, ref effects),
            "map.containsKey" => AnalyzeMapContainsKey(call, scope, ref effects, ref canReorder),
            "map.get" => AnalyzeMapGet(call, scope, ref effects, ref canReorder),
            "map.set" => AnalyzeMapSet(call, scope, ref effects, ref canReorder),
            "map.remove" => AnalyzeMapRemove(call, scope, ref effects, ref canReorder),
            "record.new" => AnalyzeRecordNew(call, scope, ref effects, ref canReorder),
            "record.get" => AnalyzeRecordGet(call, scope, ref effects, ref canReorder),
            _ => SandboxType.Unit
        };
        return true;
    }

    private SandboxType AnalyzeListEmpty(CallExpression call, ref SandboxEffect effects)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 0)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-ARITY", "list.empty expects 0 arguments", Span: call.Span));
        }

        if (call.GenericType is null)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", "list.empty requires genericType", Span: call.Span));
            return SandboxType.List(SandboxType.Unit);
        }

        CheckKnownType(call.GenericType, call.Span);
        return SandboxType.List(call.GenericType);
    }

    private SandboxType AnalyzeListOf(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        SandboxType? itemType = null;
        foreach (var arg in call.Arguments)
        {
            var current = _analyzeExpression(arg, scope, ref effects, ref canReorder);
            itemType ??= current;
            Require(current, itemType, arg.Span);
        }

        return SandboxType.List(itemType ?? SandboxType.Unit);
    }

    private SandboxType AnalyzeListCount(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 1)
        {
            Arity(call, 1);
            return SandboxType.I32;
        }

        RequireList(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        return SandboxType.I32;
    }

    private SandboxType AnalyzeListGet(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Unit;
        }

        var listType = RequireList(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        Require(
            _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder),
            SandboxType.I32,
            call.Arguments[1].Span);
        return listType?.Arguments[0] ?? SandboxType.Unit;
    }

    private SandboxType AnalyzeListAdd(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.List(SandboxType.Unit);
        }

        var listType = RequireList(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var itemType = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (listType is null)
        {
            return SandboxType.List(itemType);
        }

        Require(itemType, listType.Arguments[0], call.Arguments[1].Span);
        return listType;
    }

    private SandboxType AnalyzeMapEmpty(CallExpression call, ref SandboxEffect effects)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 0)
        {
            Arity(call, 0);
        }

        var mapType = RequireMapGeneric(call);
        return mapType ?? SandboxType.Map(SandboxType.Unit, SandboxType.Unit);
    }

    private SandboxType AnalyzeMapContainsKey(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Bool;
        }

        var mapType = RequireMap(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (mapType is not null)
        {
            Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        }

        return SandboxType.Bool;
    }

    private SandboxType AnalyzeMapGet(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Unit;
        }

        var mapType = RequireMap(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (mapType is null)
        {
            return SandboxType.Unit;
        }

        Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        return mapType.Arguments[1];
    }

    private SandboxType AnalyzeMapSet(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 3)
        {
            Arity(call, 3);
            return SandboxType.Map(SandboxType.Unit, SandboxType.Unit);
        }

        var mapType = RequireMap(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        var valueType = _analyzeExpression(call.Arguments[2], scope, ref effects, ref canReorder);
        if (mapType is null)
        {
            return SandboxType.Map(keyType, valueType);
        }

        Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        Require(valueType, mapType.Arguments[1], call.Arguments[2].Span);
        return mapType;
    }

    private SandboxType AnalyzeMapRemove(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.Arguments.Count != 2)
        {
            Arity(call, 2);
            return SandboxType.Map(SandboxType.Unit, SandboxType.Unit);
        }

        var mapType = RequireMap(
            _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder),
            call.Arguments[0].Span);
        var keyType = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (mapType is not null)
        {
            Require(keyType, mapType.Arguments[0], call.Arguments[1].Span);
        }

        return mapType ?? SandboxType.Map(keyType, SandboxType.Unit);
    }

    private SandboxType? RequireList(SandboxType actual, SourceSpan span)
    {
        if (actual.Name == "List" && actual.Arguments.Count == 1)
        {
            return actual;
        }

        _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected List<T>, got {actual}", Span: span));
        return null;
    }

    private SandboxType? RequireMap(SandboxType actual, SourceSpan span)
    {
        if (actual.Name == "Map" && actual.Arguments.Count == 2)
        {
            RequireMapKey(actual.Arguments[0], span);
            return actual;
        }

        _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected Map<K,V>, got {actual}", Span: span));
        return null;
    }

    private SandboxType? RequireMapGeneric(CallExpression call)
    {
        if (call.GenericType is null)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", "map.empty requires Map<K,V> genericType", Span: call.Span));
            return null;
        }

        CheckKnownType(call.GenericType, call.Span);
        return RequireMap(call.GenericType, call.Span);
    }

    private void RequireMapKey(SandboxType keyType, SourceSpan span)
    {
        if (keyType.IsValidMapKey(_declaredOpaqueIdTypes))
        {
            return;
        }

        _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MAP-KEY", $"map key type '{keyType}' is not supported", Span: span));
    }

    private void Require(SandboxType actual, SandboxType expected, SourceSpan span)
    {
        if (actual != expected)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected {expected}, got {actual}", Span: span));
        }
    }

    private void Arity(CallExpression call, int expected)
        => _diagnostics.Add(new SandboxDiagnostic(
            "E-CALL-ARITY",
            $"{call.Name} expects {expected} argument{(expected == 1 ? "" : "s")}",
            Span: call.Span));
}
