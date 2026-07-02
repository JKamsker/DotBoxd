using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

/// <summary>
/// Reads a <c>[HookResult]</c>-annotated positional record into a <see cref="HookResultModel"/> for the
/// builder generator. Supports a top-level readonly partial positional record struct that declares a
/// <c>bool Success</c> and a <c>string? Reason</c> field; anything else either yields no model (non-partial —
/// the builders simply are not generated) or a DBXK112 diagnostic.
/// </summary>
internal static partial class HookResultModelFactory
{
    private const string SuccessField = "Success";
    private const string ReasonField = "Reason";

    private static readonly SymbolDisplayFormat FieldTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static HookResultModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        if (type.TypeParameters.Length > 0)
        {
            return Invalid(type, declaration, $"hook result '{type.Name}' must not be generic");
        }

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            // A non-partial [HookResult] can't have IHookResult or the Ok()/Reject() builders generated for it.
            // If it doesn't already implement IHookResult, a later .Register/.RegisterLocal install (constrained
            // `where TResult : struct, IHookResult`) fails with a cryptic CS0315; surface DBXK112 so the missing
            // contract is explicit. A type that implements IHookResult by hand is valid and left alone.
            return IsValueTypeImplementingHookResult(type, context.SemanticModel.Compilation)
                ? null
                : Invalid(
                    type,
                    declaration,
                    $"hook result '{type.Name}' must be declared 'partial' so the generator can add IHookResult and "
                    + "the Ok()/Reject() builders, or it must implement IHookResult and declare those builders manually");
        }

        if (type.ContainingType is not null)
        {
            // A nested result would be emitted as a phantom top-level type; require a top-level declaration.
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a top-level type");
        }

        if (!type.IsValueType || !type.IsReadOnly)
        {
            // Builders construct via `new() { ... }` and dispatch constrains TResult to a struct, so a reference
            // (record class) result is not supported. Require readonly so generated With<Field> copies preserve
            // value-object semantics instead of exposing mutable result structs.
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a readonly record struct");
        }

        if (declaration is not RecordDeclarationSyntax { ParameterList: { } parameters })
        {
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a positional record struct");
        }

        var primary = PrimaryConstructor(type, parameters);
        if (primary is null)
        {
            return Invalid(type, declaration, $"hook result '{type.Name}' must be a positional record struct");
        }

        var fields = new List<HookResultField>(primary.Parameters.Length);
        var hasSuccess = false;
        var hasReason = false;
        foreach (var parameter in primary.Parameters)
        {
            var isSuccess = string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_Boolean;
            var isReason = string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_String
                && parameter.NullableAnnotation == NullableAnnotation.Annotated;
            hasSuccess |= isSuccess;
            hasReason |= isReason;

            var isControl = string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                || string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal);
            fields.Add(new HookResultField(
                parameter.Name,
                parameter.Type.ToDisplayString(FieldTypeFormat),
                ParameterName(parameter.Name),
                isControl));
        }

        var diagnostic = hasSuccess && hasReason
            ? null
            : new HookResultDiagnostic(
                PluginDiagnosticLocation.From(declaration.Identifier.GetLocation()),
                $"hook result '{type.Name}' must declare a 'bool Success' and a 'string? Reason' field");

        return new HookResultModel(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            DeclarationKeywords(type),
            EquatableArray<HookResultField>.FromOwned([.. fields]),
            EquatableArray<HookResultExistingMember>.FromOwned([.. ExistingMembers(type)]),
            hasSuccess,
            hasReason,
            diagnostic);
    }

    private static HookResultModel Invalid(INamedTypeSymbol type, TypeDeclarationSyntax declaration, string message)
        => new(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            string.Empty,
            EquatableArray<HookResultField>.FromOwned([]),
            EquatableArray<HookResultExistingMember>.FromOwned([]),
            HasSuccess: false,
            HasReason: false,
            new HookResultDiagnostic(PluginDiagnosticLocation.From(declaration.Identifier.GetLocation()), message));

    internal static bool CanSatisfyHookResult(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (type.TypeParameters.Length > 0)
        {
            return false;
        }

        return IsValueTypeImplementingHookResult(type, compilation) ||
            IsValidGeneratedHookResult(type, cancellationToken);
    }

    private static bool IsValueTypeImplementingHookResult(INamedTypeSymbol type, Compilation compilation)
    {
        if (!type.IsValueType)
        {
            return false;
        }

        var hookResult = compilation.GetTypeByMetadataName("DotBoxD.Abstractions.IHookResult");
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

    private static bool IsValidGeneratedHookResult(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (type is not { IsValueType: true, IsReadOnly: true, IsRecord: true, ContainingType: null } ||
            type.TypeParameters.Length > 0 ||
            !HasHookResultAttribute(type))
        {
            return false;
        }

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not RecordDeclarationSyntax
                {
                    ParameterList: { } parameters
                } declaration ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                PrimaryConstructor(type, parameters) is not { } primary)
            {
                continue;
            }

            if (HasControlConstructorParameters(primary))
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool HasControlConstructorParameters(IMethodSymbol primary)
    {
        var hasSuccess = false;
        var hasReason = false;
        foreach (var parameter in primary.Parameters)
        {
            hasSuccess |= string.Equals(parameter.Name, SuccessField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_Boolean;
            hasReason |= string.Equals(parameter.Name, ReasonField, StringComparison.Ordinal)
                && parameter.Type.SpecialType == SpecialType.System_String
                && parameter.NullableAnnotation == NullableAnnotation.Annotated;
        }

        return hasSuccess && hasReason;
    }

    // The positional record's primary constructor: the instance constructor that is neither the implicit
    // parameterless struct constructor nor the synthesized single-parameter copy constructor.
    private static IMethodSymbol? PrimaryConstructor(
        INamedTypeSymbol type,
        ParameterListSyntax parameters)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length != parameters.Parameters.Count)
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < parameters.Parameters.Count; i++)
            {
                if (!string.Equals(
                        constructor.Parameters[i].Name,
                        parameters.Parameters[i].Identifier.ValueText,
                        StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return constructor;
            }
        }

        return null;
    }

    private static string DeclarationKeywords(INamedTypeSymbol type)
    {
        var accessibilityPrefix = type.DeclaredAccessibility switch
        {
            Accessibility.Public => "public ",
            Accessibility.Internal => "internal ",
            _ => string.Empty
        };
        var readOnlyPrefix = type.IsValueType && type.IsReadOnly ? "readonly " : string.Empty;
        var structSuffix = type.IsValueType ? " struct" : string.Empty;
        return accessibilityPrefix + readOnlyPrefix + "partial record" + structSuffix;
    }

    private static string ParameterName(string fieldName)
    {
        var camel = char.ToLowerInvariant(fieldName[0]) + fieldName.Substring(1);
        return SyntaxFacts.GetKeywordKind(camel) == SyntaxKind.None &&
               SyntaxFacts.GetContextualKeywordKind(camel) == SyntaxKind.None
            ? camel
            : "@" + camel;
    }
}
