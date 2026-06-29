using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class MethodSignatureFormatter
{
    // Compile against the repo's older Roslyn package, but preserve the C# 13 anti-constraint
    // when the generator is hosted by a newer compiler that exposes this symbol property.
    private static readonly PropertyInfo? s_allowsRefLikeTypeProperty =
        typeof(ITypeParameterSymbol).GetProperty("AllowsRefLikeType");

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string GetTypeParameterList(IMethodSymbol method, CancellationToken ct)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var parameter in method.TypeParameters)
        {
            ct.ThrowIfCancellationRequested();
            names.Add(IdentifierHelpers.EscapeIdentifier(parameter.Name));
        }

        return "<" + string.Join(", ", names) + ">";
    }

    public static string GetConstraintClauses(IMethodSymbol method, CancellationToken ct)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        foreach (var typeParameter in method.TypeParameters)
        {
            ct.ThrowIfCancellationRequested();

            var constraints = new List<string>();
            if (typeParameter.HasReferenceTypeConstraint)
            {
                constraints.Add(
                    typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                        ? "class?"
                        : "class");
            }
            else if (typeParameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (typeParameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (typeParameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            foreach (var constraintType in typeParameter.ConstraintTypes)
            {
                ct.ThrowIfCancellationRequested();
                constraints.Add(constraintType.ToDisplayString(s_qualifiedFormat));
            }

            if (typeParameter.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            if (AllowsRefLikeType(typeParameter))
            {
                constraints.Add("allows ref struct");
            }

            if (constraints.Count > 0)
            {
                clauses.Add($" where {IdentifierHelpers.EscapeIdentifier(typeParameter.Name)} : {string.Join(", ", constraints)}");
            }
        }

        return string.Concat(clauses);
    }

    private static bool AllowsRefLikeType(ITypeParameterSymbol typeParameter) =>
        s_allowsRefLikeTypeProperty?.GetValue(typeParameter) is true;
}
