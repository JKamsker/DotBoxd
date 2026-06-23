using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation;

internal sealed partial class FunctionAnalyzer
{
    private bool AnalyzeStatement(
        Statement statement,
        FunctionScope scope,
        SandboxType returnType,
        ref SandboxEffect effects,
        ref bool canReorder,
        int loopDepth)
    {
        switch (statement)
        {
            case AssignmentStatement assignment:
                var assignmentType = AnalyzeExpression(
                    assignment.Value,
                    scope,
                    ref effects,
                    ref canReorder);
                scope.Set(assignment.Name, assignmentType, _diagnostics, assignment.Span);
                return false;
            case ReturnStatement ret:
                Require(
                    AnalyzeExpression(ret.Value, scope, ref effects, ref canReorder),
                    returnType,
                    ret.Span);
                return true;
            case ExpressionStatement expr:
                AnalyzeExpression(expr.Value, scope, ref effects, ref canReorder);
                return false;
            case IfStatement branch:
                return AnalyzeBranch(branch, scope, returnType, ref effects, ref canReorder, loopDepth);
            case WhileStatement loop:
                Require(
                    AnalyzeExpression(loop.Condition, scope, ref effects, ref canReorder),
                    SandboxType.Bool,
                    loop.Span);
                AnalyzeBlock(loop.Body, scope.Clone(), returnType, ref effects, ref canReorder, loopDepth + 1);
                return false;
            case ForRangeStatement range:
                AnalyzeRange(range, scope, returnType, ref effects, ref canReorder, loopDepth);
                return false;
            case ContinueStatement:
                RequireInsideLoop("continue", loopDepth, statement.Span);
                return false;
            case BreakStatement:
                RequireInsideLoop("break", loopDepth, statement.Span);
                return false;
            default:
                _diagnostics.Add(new SandboxDiagnostic("E-STMT-UNKNOWN", $"unsupported statement '{statement.GetType().Name}'", Span: statement.Span));
                return false;
        }
    }

    private bool AnalyzeBranch(
        IfStatement branch,
        FunctionScope scope,
        SandboxType returnType,
        ref SandboxEffect effects,
        ref bool canReorder,
        int loopDepth)
    {
        Require(
            AnalyzeExpression(branch.Condition, scope, ref effects, ref canReorder),
            SandboxType.Bool,
            branch.Span);
        var thenReturns = AnalyzeBlock(branch.Then, scope.Clone(), returnType, ref effects, ref canReorder, loopDepth);
        var elseReturns = AnalyzeBlock(branch.Else, scope.Clone(), returnType, ref effects, ref canReorder, loopDepth);
        return thenReturns && elseReturns;
    }

    private void AnalyzeRange(
        ForRangeStatement range,
        FunctionScope scope,
        SandboxType returnType,
        ref SandboxEffect effects,
        ref bool canReorder,
        int loopDepth)
    {
        Require(AnalyzeExpression(range.Start, scope, ref effects, ref canReorder), SandboxType.I32, range.Span);
        Require(AnalyzeExpression(range.End, scope, ref effects, ref canReorder), SandboxType.I32, range.Span);
        var child = scope.Clone();
        child.Set(range.LocalName, SandboxType.I32, _diagnostics, range.Span);
        AnalyzeBlock(range.Body, child, returnType, ref effects, ref canReorder, loopDepth + 1);
    }

    private void RequireInsideLoop(string keyword, int loopDepth, SourceSpan span)
    {
        if (loopDepth == 0)
        {
            _diagnostics.Add(new SandboxDiagnostic(
                "E-LOOP-CONTROL",
                $"'{keyword}' is only valid inside a loop",
                Span: span));
        }
    }

    private void AnalyzeDeadStatement(Statement statement, FunctionScope scope, SandboxType returnType, int loopDepth)
    {
        var ignoredEffects = SandboxEffect.None;
        var ignoredCanReorder = true;
        _ = AnalyzeStatement(
            statement,
            scope,
            returnType,
            ref ignoredEffects,
            ref ignoredCanReorder,
            loopDepth);
    }

    private bool AnalyzeBlock(
        IReadOnlyList<Statement> block,
        FunctionScope scope,
        SandboxType returnType,
        ref SandboxEffect effects,
        ref bool canReorder,
        int loopDepth)
    {
        var alwaysReturns = false;
        foreach (var statement in block)
        {
            if (alwaysReturns)
            {
                AnalyzeDeadStatement(statement, scope, returnType, loopDepth);
                continue;
            }

            alwaysReturns = AnalyzeStatement(
                statement,
                scope,
                returnType,
                ref effects,
                ref canReorder,
                loopDepth);
        }

        return alwaysReturns;
    }
}
