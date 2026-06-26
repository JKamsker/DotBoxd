using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    public static bool CanReconstructWithObjectInitializer(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation = null)
    {
        if (fields.Count == 0 || (!type.IsValueType && !HasAccessibleParameterlessConstructor(type, compilation)))
        {
            return false;
        }

        return CanReconstructFromAssignedFields(fields, ObjectInitializerAssigned(fields, compilation), compilation);
    }

    public static bool CanReconstructFromAssignedFields(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        Compilation? compilation = null)
    {
        var reconstructable = ObjectInitializerAssigned(fields, assigned, compilation);
        while (TryMarkDerivedField(fields, reconstructable))
        {
        }

        return reconstructable.All(static item => item);
    }

    public static bool IsDerivedFromAssignedFields(
        RecordMember member,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned)
    {
        if (member.Symbol is not IPropertySymbol
            {
                GetMethod: not null,
                SetMethod: null
            } property)
        {
            return false;
        }

        if (TryGetDerivedGetterExpression(property) is not { } body)
        {
            return false;
        }

        var assignedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                assignedNames.Add(fields[i].Name);
            }
        }

        return IsExpressionOverAssignedFields(body, assignedNames);
    }

    private static bool[] ObjectInitializerAssigned(
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation = null)
    {
        var assigned = new bool[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            assigned[i] = IsObjectInitializerWritable(fields[i], compilation);
        }

        return assigned;
    }

    private static bool[] ObjectInitializerAssigned(
        IReadOnlyList<RecordMember> fields,
        bool[] alreadyAssigned,
        Compilation? compilation)
    {
        var assigned = new bool[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            assigned[i] = alreadyAssigned[i] || IsObjectInitializerWritable(fields[i], compilation);
        }

        return assigned;
    }

    private static bool TryMarkDerivedField(IReadOnlyList<RecordMember> fields, bool[] assigned)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (!assigned[i] && IsDerivedFromAssignedFields(fields[i], fields, assigned))
            {
                assigned[i] = true;
                return true;
            }
        }

        return false;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type, Compilation? compilation)
        => type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            IsAccessibleFromGeneratedCode(constructor, compilation));

    private static bool IsExpressionOverAssignedFields(
        ExpressionSyntax expression,
        ISet<string> assignedNames)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                IsExpressionOverAssignedFields(parenthesized.Expression, assignedNames),
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax identifier => assignedNames.Contains(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisMember =>
                assignedNames.Contains(thisMember.Name.Identifier.ValueText),
            PrefixUnaryExpressionSyntax unary => IsSupportedUnary(unary) &&
                IsExpressionOverAssignedFields(unary.Operand, assignedNames),
            BinaryExpressionSyntax binary =>
                IsExpressionOverAssignedFields(binary.Left, assignedNames) &&
                IsExpressionOverAssignedFields(binary.Right, assignedNames),
            _ => false
        };

    private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax unary)
        => unary.IsKind(SyntaxKind.LogicalNotExpression) ||
           unary.IsKind(SyntaxKind.UnaryMinusExpression) ||
           unary.IsKind(SyntaxKind.UnaryPlusExpression);

    internal static ExpressionSyntax? TryGetDerivedGetterExpression(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody is { } arrow)
            {
                return arrow.Expression;
            }

            var getter = declaration.AccessorList?.Accessors
                .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody is { } getterArrow)
            {
                return getterArrow.Expression;
            }

            if (getter?.Body is { Statements.Count: 1 } getterBody &&
                getterBody.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                return returned;
            }
        }

        return null;
    }
}
