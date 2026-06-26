using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static List<InvokeAsyncSyncOut> FindSyncOuts(
        BlockSyntax block,
        InvokeAsyncCaptureParameter captureParameter,
        SemanticModel model)
    {
        ValidateExplicitCaptureMutations(block, captureParameter, model);
        var syncOuts = new List<InvokeAsyncSyncOut>();
        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!TryCaptureMember(assignment.Left, captureParameter.Name, model, out var member, out var target))
            {
                continue;
            }

            var recordMember = DotBoxDRpcTypeMapper.RecordFields(captureParameter.Type)
                .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.Symbol, target));
            if (recordMember.Symbol is null)
            {
                throw new NotSupportedException(
                    $"InvokeAsync capture member '{target.Name}' must be a public marshalled field or property.");
            }

            if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
            {
                throw new NotSupportedException(
                    $"InvokeAsync capture member '{recordMember.Name}' must use a simple assignment.");
            }

            ValidateWritableCapture(recordMember, model.Compilation);
            if (syncOuts.Any(item => string.Equals(item.TargetName, recordMember.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            syncOuts.Add(new InvokeAsyncSyncOut(
                recordMember.Name,
                recordMember.Type,
                "__syncOut_" + recordMember.Name,
                member));
        }

        return syncOuts;
    }

    private static void ValidateWritableCapture(RecordMember member, Compilation compilation)
    {
        switch (member.Symbol)
        {
            case IPropertySymbol property
                when property.SetMethod is not null &&
                     !property.SetMethod.IsInitOnly &&
                     IsAccessibleFromGeneratedCode(property.SetMethod, compilation):
                return;
            case IPropertySymbol property:
                throw new NotSupportedException(
                    $"InvokeAsync capture property '{property.Name}' must be assigned with an accessible set accessor.");
            case IFieldSymbol field
                when !field.IsReadOnly &&
                     !field.IsConst &&
                     IsAccessibleFromGeneratedCode(field, compilation):
                return;
            case IFieldSymbol field:
                throw new NotSupportedException(
                    $"InvokeAsync capture field '{field.Name}' must be writable and accessible from generated code.");
            default:
                throw new NotSupportedException(
                    $"InvokeAsync capture member '{member.Name}' must be a writable property or field.");
        }
    }

    private static bool TryCaptureMember(
        ExpressionSyntax expression,
        string captureParameterName,
        SemanticModel model,
        out MemberAccessExpressionSyntax member,
        out ISymbol target)
    {
        member = null!;
        target = null!;
        if (expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver
            } found ||
            !string.Equals(receiver.Identifier.ValueText, captureParameterName, StringComparison.Ordinal))
        {
            return false;
        }

        target = model.GetSymbolInfo(found).Symbol switch
        {
            IPropertySymbol property => property,
            IFieldSymbol field => field,
            _ => null!
        };

        if (target is null)
        {
            return false;
        }

        member = found;
        return true;
    }

    private static bool IsAccessibleFromGeneratedCode(ISymbol symbol, Compilation compilation)
        => compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);

    private static IReadOnlyList<(string Name, ExpressionSyntax Value)> CreateLeadingLocals(
        IReadOnlyList<InvokeAsyncSyncOut> syncOuts)
        => syncOuts
            .Where(static syncOut => syncOut.Initializer is not null)
            .Select(syncOut => (syncOut.LocalName, syncOut.Initializer!))
            .ToArray();

    private static string? LowerCaptureAssignment(
        AssignmentExpressionSyntax assignment,
        InvokeAsyncCaptureParameter captureParameter,
        IReadOnlyList<InvokeAsyncSyncOut> syncOuts,
        Func<ExpressionSyntax, string> lower)
    {
        if (!TryCaptureMember(assignment.Left, captureParameter.Name, captureParameter.Type, out var propertyName))
        {
            return null;
        }

        var syncOut = syncOuts.FirstOrDefault(item => string.Equals(item.TargetName, propertyName, StringComparison.Ordinal));
        if (syncOut is null)
        {
            throw new NotSupportedException(
                $"InvokeAsync capture member '{propertyName}' must be a writable marshalled field or property.");
        }

        return DotBoxDRpcJsonLowerer.SetGeneratedLocal(syncOut.LocalName, lower(assignment.Right));
    }

    private static string? LowerCaptureRead(
        ExpressionSyntax expression,
        InvokeAsyncCaptureParameter captureParameter,
        IReadOnlyList<InvokeAsyncSyncOut> syncOuts)
    {
        if (!TryCaptureMember(expression, captureParameter.Name, captureParameter.Type, out var propertyName))
        {
            return null;
        }

        var syncOut = syncOuts.FirstOrDefault(item => string.Equals(item.TargetName, propertyName, StringComparison.Ordinal));
        return syncOut is null ? null : DotBoxDRpcJsonLowerer.Var(syncOut.LocalName);
    }

    private static bool TryCaptureMember(
        ExpressionSyntax expression,
        string captureParameterName,
        INamedTypeSymbol captureType,
        out string propertyName)
    {
        propertyName = string.Empty;
        if (expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver
            } member ||
            !string.Equals(receiver.Identifier.ValueText, captureParameterName, StringComparison.Ordinal))
        {
            return false;
        }

        var name = member.Name.Identifier.ValueText;
        if (!DotBoxDRpcTypeMapper.RecordFields(captureType)
                .Any(property => string.Equals(property.Name, name, StringComparison.Ordinal)))
        {
            return false;
        }

        propertyName = name;
        return true;
    }

    private static string CaptureParametersJson(InvokeAsyncCaptureParameter captureParameter)
        => "[{\"name\":" + DotBoxDRpcJsonLowerer.Str(captureParameter.Name) +
           ",\"type\":" + DotBoxDRpcTypeMapper.JsonType(captureParameter.Type) + "}]";

    private static string CaptureArgumentsExpression(ITypeSymbol captureType)
        => "new global::DotBoxD.Plugins.KernelRpcValue[] { " +
           InvokeAsyncArgumentWriterSource.WriteExpression(captureType, "captures") +
           " }";
}
