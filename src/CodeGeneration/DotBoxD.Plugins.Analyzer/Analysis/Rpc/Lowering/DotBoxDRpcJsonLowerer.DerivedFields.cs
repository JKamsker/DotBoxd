using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    // Builds the wire slot for a record field that has no constructor parameter — a derived/get-only member such
    // as `public int Sum => X + Y;`. The member is recomputed by the runtime on decode, but the sandbox record
    // value still needs a slot for it, and an in-sandbox read of the member reads that slot, so it must hold the
    // correct value. We lower the member's getter over the constructor-bound members (a name-based substitution:
    // each member reference becomes the already-lowered constructor argument for that member). Only a simple
    // expression over the constructor's members is supported — anything else, or a getter whose source is not
    // available (e.g. the record is declared in another assembly), is a clear diagnostic rather than a guess.
    private string LowerDerivedField(
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        string[] args,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        if (derived.Symbol is not IPropertySymbol { GetMethod: not null } property ||
            TryGetDerivedGetterExpression(property) is not { } body)
        {
            throw new System.NotSupportedException(
                $"Server extension constructor for '{named.Name}' cannot reconstruct the derived member '{derived.Name}' " +
                "(no inspectable getter is available — for example it is declared in another assembly). Construct " +
                $"'{named.Name}' where the value is available, or expose '{derived.Name}' as a constructor parameter.");
        }

        var memberBindings = new Dictionary<string, string>(System.StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                memberBindings[fields[i].Name] = args[i];
            }
        }

        return LowerDerivedExpression(body, memberBindings, named, derived);
    }

    private string LowerDerivedExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> memberBindings,
        INamedTypeSymbol named,
        RecordMember derived)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return LowerDerivedExpression(parenthesized.Expression, memberBindings, named, derived);

            case LiteralExpressionSyntax literal:
                return LiteralJson(literal.Token.Value);

            case IdentifierNameSyntax identifier
                when memberBindings.TryGetValue(identifier.Identifier.ValueText, out var bound):
                return bound;

            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisMember
                when memberBindings.TryGetValue(thisMember.Name.Identifier.ValueText, out var boundThis):
                return boundThis;

            case PrefixUnaryExpressionSyntax unary:
                return unary.Kind() switch
                {
                    SyntaxKind.LogicalNotExpression => Obj(
                        ("unary", Str("not")),
                        ("operand", LowerDerivedExpression(unary.Operand, memberBindings, named, derived))),
                    SyntaxKind.UnaryMinusExpression => Obj(
                        ("unary", Str("-")),
                        ("operand", LowerDerivedExpression(unary.Operand, memberBindings, named, derived))),
                    _ => throw DerivedNotSupported(named, derived),
                };

            case BinaryExpressionSyntax binary:
                return BinaryJson(
                    JsonBinaryOperator(binary),
                    LowerDerivedExpression(binary.Left, memberBindings, named, derived),
                    LowerDerivedExpression(binary.Right, memberBindings, named, derived));

            default:
                throw DerivedNotSupported(named, derived);
        }
    }

    private static System.NotSupportedException DerivedNotSupported(INamedTypeSymbol named, RecordMember derived)
        => new(
            $"Server extension constructor for '{named.Name}' cannot build the derived member '{derived.Name}' in the " +
            "sandbox: its getter is not a simple expression over the constructor's parameters. Pass the value as a " +
            $"constructor parameter, or construct '{named.Name}' where the value is available.");

    private static ExpressionSyntax? TryGetDerivedGetterExpression(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                continue;
            }

            // `public int Sum => X + Y;`
            if (declaration.ExpressionBody is { } arrow)
            {
                return arrow.Expression;
            }

            var getter = declaration.AccessorList?.Accessors
                .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));

            // `public int Sum { get => X + Y; }`
            if (getter?.ExpressionBody is { } getterArrow)
            {
                return getterArrow.Expression;
            }

            // `public int Sum { get { return X + Y; } }`
            if (getter?.Body is { } getterBody &&
                getterBody.Statements.Count == 1 &&
                getterBody.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                return returned;
            }
        }

        return null;
    }
}
