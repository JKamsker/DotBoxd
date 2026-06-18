using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

/// <summary>
/// Type-checking for the <c>record.*</c> intrinsics. <c>record.new</c> takes a <c>Record</c>
/// <see cref="CallExpression.GenericType"/> and one argument per field (validated against the declared
/// field types); <c>record.get</c> reads a field by a <b>constant</b> index so the result type is known
/// statically. Split from <see cref="CollectionCallAnalyzer"/> to keep each file within the size budget.
/// </summary>
internal sealed partial class CollectionCallAnalyzer
{
    private SandboxType AnalyzeRecordNew(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        effects |= SandboxEffect.Alloc;
        if (call.GenericType is not { } recordType || !recordType.IsRecord)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-GENERIC", "record.new requires a Record genericType", Span: call.Span));
            return AnalyzeRecordFieldsFallback(call, scope, ref effects, ref canReorder);
        }

        CheckKnownType(recordType, call.Span);
        if (call.Arguments.Count != recordType.Arguments.Count)
        {
            _diagnostics.Add(new SandboxDiagnostic(
                "E-CALL-ARITY",
                $"record.new expects {recordType.Arguments.Count} field argument(s)",
                Span: call.Span));
            return recordType;
        }

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var fieldType = _analyzeExpression(call.Arguments[i], scope, ref effects, ref canReorder);
            Require(fieldType, recordType.Arguments[i], call.Arguments[i].Span);
        }

        return recordType;
    }

    private SandboxType AnalyzeRecordFieldsFallback(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder)
    {
        foreach (var argument in call.Arguments)
        {
            _ = _analyzeExpression(argument, scope, ref effects, ref canReorder);
        }

        return SandboxType.Unit;
    }

    private SandboxType AnalyzeRecordGet(
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

        var recordType = _analyzeExpression(call.Arguments[0], scope, ref effects, ref canReorder);
        // The field index must be a constant so the field type is statically known; analyze the second
        // operand for effects either way.
        _ = _analyzeExpression(call.Arguments[1], scope, ref effects, ref canReorder);
        if (!recordType.IsRecord)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected Record, got {recordType}", Span: call.Arguments[0].Span));
            return SandboxType.Unit;
        }

        if (call.Arguments[1] is not LiteralExpression { Value: I32Value index })
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-RECORD-INDEX", "record.get field index must be a constant I32", Span: call.Arguments[1].Span));
            return SandboxType.Unit;
        }

        if (index.Value < 0 || index.Value >= recordType.Arguments.Count)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-CALL-RECORD-INDEX", $"record.get field index {index.Value} is out of range", Span: call.Arguments[1].Span));
            return SandboxType.Unit;
        }

        return recordType.Arguments[index.Value];
    }

    private void CheckKnownType(SandboxType type, SourceSpan span)
    {
        if (!type.IsKnown(_declaredOpaqueIdTypes))
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{type}'", Span: span));
        }
    }

    private static bool IsCollectionCall(string name)
        => name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove"
            or "record.new" or "record.get";
}
