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
        var syncOuts = new List<InvokeAsyncSyncOut>();
        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!TryCaptureMember(assignment.Left, captureParameter.Name, model, out var member, out var property))
            {
                continue;
            }

            if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                property.SetMethod is null ||
                property.SetMethod.IsInitOnly ||
                !IsAccessibleFromGeneratedCode(property.SetMethod))
            {
                throw new NotSupportedException(
                    $"InvokeAsync capture property '{property.Name}' must be assigned with an accessible set accessor.");
            }

            if (syncOuts.Any(item => string.Equals(item.TargetName, property.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            syncOuts.Add(new InvokeAsyncSyncOut(property.Name, property.Type, "__syncOut_" + property.Name, member));
        }

        return syncOuts;
    }

    private static bool TryCaptureMember(
        ExpressionSyntax expression,
        string captureParameterName,
        SemanticModel model,
        out MemberAccessExpressionSyntax member,
        out IPropertySymbol property)
    {
        member = null!;
        property = null!;
        if (expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver
            } found ||
            !string.Equals(receiver.Identifier.ValueText, captureParameterName, StringComparison.Ordinal) ||
            model.GetSymbolInfo(found).Symbol is not IPropertySymbol foundProperty)
        {
            return false;
        }

        member = found;
        property = foundProperty;
        return true;
    }

    private static bool IsAccessibleFromGeneratedCode(IMethodSymbol setMethod)
        => setMethod.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Internal
            or Accessibility.ProtectedOrInternal;

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

        var syncOut = syncOuts.Single(item => string.Equals(item.TargetName, propertyName, StringComparison.Ordinal));
        return DotBoxDRpcJsonLowerer.SetGeneratedLocal(syncOut.LocalName, lower(assignment.Right));
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
