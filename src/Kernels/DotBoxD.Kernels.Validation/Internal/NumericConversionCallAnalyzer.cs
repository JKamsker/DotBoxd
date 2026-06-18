using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal sealed class NumericConversionCallAnalyzer
{
    private readonly List<SandboxDiagnostic> _diagnostics;
    private readonly ExpressionAnalyzer _analyzeExpression;

    public NumericConversionCallAnalyzer(List<SandboxDiagnostic> diagnostics, ExpressionAnalyzer analyzeExpression)
    {
        _diagnostics = diagnostics;
        _analyzeExpression = analyzeExpression;
    }

    public bool TryAnalyze(
        CallExpression call,
        FunctionScope scope,
        ref SandboxEffect effects,
        ref bool canReorder,
        out SandboxType type)
    {
        type = SandboxType.Unit;
        if (call.Name is not ("numeric.toI64" or "numeric.toF64"))
        {
            return false;
        }

        if (call.Arguments.Count != 1)
        {
            _diagnostics.Add(new SandboxDiagnostic(
                "E-CALL-ARITY",
                $"call '{call.Name}' expects 1 argument",
                Span: call.Span));
            type = TargetType(call);
            return true;
        }

        var argumentType = _analyzeExpression(
            call.Arguments[0],
            scope,
            ref effects,
            ref canReorder);
        type = TargetType(call);
        if (call.Name == "numeric.toI64")
        {
            Require(argumentType, SandboxType.I32, call.Arguments[0].Span);
            return true;
        }

        if (argumentType == SandboxType.I32 || argumentType == SandboxType.I64)
        {
            return true;
        }

        _diagnostics.Add(new SandboxDiagnostic(
            "E-TYPE-MISMATCH",
            $"expected I32 or I64, got {argumentType}",
            Span: call.Arguments[0].Span));
        return true;
    }

    private static SandboxType TargetType(CallExpression call)
        => call.Name == "numeric.toI64" ? SandboxType.I64 : SandboxType.F64;

    private void Require(SandboxType actual, SandboxType expected, SourceSpan span)
    {
        if (actual != expected)
        {
            _diagnostics.Add(new SandboxDiagnostic("E-TYPE-MISMATCH", $"expected {expected}, got {actual}", Span: span));
        }
    }
}
