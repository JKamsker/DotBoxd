using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static TypeSyntax? DeclaredTypeSyntax(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (expression is CastExpressionSyntax { Type: { } castType })
        {
            return castType;
        }

        if (expression is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression) &&
            asExpression.Right is TypeSyntax asType)
        {
            return asType;
        }

        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            return AwaitedTypeSyntax(awaitExpression, model, cancellationToken);
        }

        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } resultAccess &&
            TaskResultTypeSyntax(resultAccess, model, cancellationToken) is { } resultType)
        {
            return resultType;
        }

        var symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is null &&
            expression is MemberAccessExpressionSyntax { Name: SimpleNameSyntax name })
        {
            symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
        }

        return symbol switch
        {
            IParameterSymbol parameter => ParameterTypeSyntax(parameter, cancellationToken),
            ILocalSymbol local => LocalTypeSyntax(local, model, cancellationToken),
            IFieldSymbol field => FieldTypeSyntax(field, cancellationToken),
            IPropertySymbol property => PropertyTypeSyntax(property, cancellationToken),
            IMethodSymbol method => MethodReturnTypeSyntax(method, cancellationToken),
            _ => null
        };
    }

    private static TypeSyntax? ParameterTypeSyntax(IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        foreach (var reference in parameter.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is ParameterSyntax { Type: { } typeSyntax })
            {
                return typeSyntax;
            }
        }

        return null;
    }

    private static TypeSyntax? MethodReturnTypeSyntax(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(cancellationToken))
            {
                case MethodDeclarationSyntax { ReturnType: { } returnType }:
                    return returnType;
                case LocalFunctionStatementSyntax { ReturnType: { } returnType }:
                    return returnType;
            }
        }

        return null;
    }

    private static TypeSyntax? FieldTypeSyntax(IFieldSymbol field, CancellationToken cancellationToken)
    {
        foreach (var reference in field.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Type: { } typeSyntax,
                        Parent: FieldDeclarationSyntax
                    }
                })
            {
                return typeSyntax;
            }
        }

        return null;
    }

    private static TypeSyntax? PropertyTypeSyntax(IPropertySymbol property, CancellationToken cancellationToken)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(cancellationToken))
            {
                case PropertyDeclarationSyntax { Type: { } typeSyntax }:
                    return typeSyntax;
                case IndexerDeclarationSyntax { Type: { } typeSyntax }:
                    return typeSyntax;
                case ParameterSyntax { Type: { } typeSyntax }:
                    return typeSyntax;
            }
        }

        return null;
    }

    private static TypeSyntax? AwaitedTypeSyntax(
        AwaitExpressionSyntax awaitExpression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var awaitedExpression = HookChainAliasResolver.UnwrapTransparentExpression(awaitExpression.Expression);
        return DeclaredTypeSyntax(awaitedExpression, model, cancellationToken) is { } typeSyntax
            ? AwaitedTypeArgumentSyntax(typeSyntax)
            : null;
    }

    private static TypeSyntax? TaskResultTypeSyntax(
        MemberAccessExpressionSyntax resultAccess,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var taskExpression = HookChainAliasResolver.UnwrapTransparentExpression(resultAccess.Expression);
        return DeclaredTypeSyntax(taskExpression, model, cancellationToken) is { } typeSyntax
            ? AwaitedTypeArgumentSyntax(typeSyntax)
            : null;
    }

    private static TypeSyntax? AwaitedTypeArgumentSyntax(TypeSyntax typeSyntax)
    {
        if (typeSyntax is NullableTypeSyntax nullable)
        {
            typeSyntax = nullable.ElementType;
        }

        return typeSyntax switch
        {
            GenericNameSyntax generic => AwaitedTypeArgumentSyntax(generic),
            QualifiedNameSyntax { Right: GenericNameSyntax generic } => AwaitedTypeArgumentSyntax(generic),
            AliasQualifiedNameSyntax { Name: GenericNameSyntax generic } => AwaitedTypeArgumentSyntax(generic),
            _ => null
        };
    }

    private static TypeSyntax? AwaitedTypeArgumentSyntax(GenericNameSyntax generic)
        => generic.TypeArgumentList.Arguments.Count == 1 &&
           generic.Identifier.ValueText is "Task" or "ValueTask"
            ? generic.TypeArgumentList.Arguments[0]
            : null;

    private static TypeSyntax? LocalTypeSyntax(
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(cancellationToken))
            {
                case VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Type: { } typeSyntax }
                } when !typeSyntax.IsVar:
                    return typeSyntax;
                case VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer,
                    Parent: VariableDeclarationSyntax { Type: { } typeSyntax }
                } when typeSyntax.IsVar:
                    return InferredInitializerTypeSyntax(initializer, model, cancellationToken);
                case SingleVariableDesignationSyntax
                {
                    Parent: DeclarationPatternSyntax { Type: { } typeSyntax }
                }:
                    return typeSyntax;
                case SingleVariableDesignationSyntax
                {
                    Parent: DeclarationExpressionSyntax { Type: { } typeSyntax }
                } when !typeSyntax.IsVar:
                    return typeSyntax;
                case SingleVariableDesignationSyntax
                {
                    Parent: DeclarationExpressionSyntax { Type: { } typeSyntax } declaration
                } when typeSyntax.IsVar:
                    return OutDeclarationParameterTypeSyntax(declaration, model, cancellationToken) ??
                        SymbolTypeSyntax(local.Type);
            }
        }

        return SymbolTypeSyntax(local.Type);
    }

    private static TypeSyntax? InferredInitializerTypeSyntax(
        ExpressionSyntax initializer,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var expression = HookChainAliasResolver.UnwrapTransparentExpression(initializer);
        return DeclaredTypeSyntax(expression, model, cancellationToken) ??
            SemanticExpressionTypeSyntax(expression, model, cancellationToken);
    }

    private static TypeSyntax? SemanticExpressionTypeSyntax(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var typeInfo = model.GetTypeInfo(expression, cancellationToken);
        return SymbolTypeSyntax(typeInfo.ConvertedType ?? typeInfo.Type);
    }

    private static TypeSyntax? SymbolTypeSyntax(ITypeSymbol? type)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return null;
        }

        return SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static TypeSyntax? OutDeclarationParameterTypeSyntax(
        DeclarationExpressionSyntax declaration,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (declaration.Parent is not ArgumentSyntax argument ||
            !argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) ||
            model.GetOperation(argument, cancellationToken) is not IArgumentOperation { Parameter: { } parameter })
        {
            return null;
        }

        return ParameterTypeSyntax(parameter, cancellationToken);
    }
}
