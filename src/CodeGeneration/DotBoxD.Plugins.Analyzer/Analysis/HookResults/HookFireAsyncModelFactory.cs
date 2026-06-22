using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncModelFactory
{
    private const string HookResultInterface = "DotBoxD.Abstractions.IHookResult";
    private const string SuccessField = "Success";
    private const string ReasonField = "Reason";

    public static HookFireAsyncModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol contextType ||
            contextType.TypeParameters.Length > 0)
        {
            return null;
        }

        foreach (var attribute in context.Attributes)
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.HookAttribute,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 2 ||
                attribute.ConstructorArguments[1].Value is not INamedTypeSymbol resultType ||
                resultType.TypeParameters.Length > 0 ||
                !CanSatisfyHookResult(resultType, context.SemanticModel.Compilation, cancellationToken))
            {
                continue;
            }

            return new HookFireAsyncModel(
                contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return null;
    }

    private static bool CanSatisfyHookResult(
        INamedTypeSymbol resultType,
        Compilation compilation,
        CancellationToken cancellationToken)
        => ImplementsHookResult(resultType, compilation) ||
           IsValidGeneratedHookResultCandidate(resultType, cancellationToken);

    private static bool ImplementsHookResult(INamedTypeSymbol type, Compilation compilation)
    {
        var hookResult = compilation.GetTypeByMetadataName(HookResultInterface);
        if (hookResult is null)
        {
            return false;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface, hookResult))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidGeneratedHookResultCandidate(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
        => type is { IsValueType: true, IsReadOnly: true, IsRecord: true, ContainingType: null } &&
           HasHookResultAttribute(type) &&
           HasPartialDeclaration(type, cancellationToken) &&
           HasControlFields(type);

    private static bool HasHookResultAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.HookResultAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPartialDeclaration(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasControlFields(INamedTypeSymbol type)
    {
        var hasSuccess = false;
        var hasReason = false;
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            hasSuccess |= string.Equals(property.Name, SuccessField, StringComparison.Ordinal) &&
                property.Type.SpecialType == SpecialType.System_Boolean;
            hasReason |= string.Equals(property.Name, ReasonField, StringComparison.Ordinal) &&
                property.Type.SpecialType == SpecialType.System_String &&
                property.NullableAnnotation == NullableAnnotation.Annotated;
        }

        return hasSuccess && hasReason;
    }
}
