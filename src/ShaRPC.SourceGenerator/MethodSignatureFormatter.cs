using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class MethodSignatureFormatter
{
    private static readonly SymbolDisplayFormat s_qualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static string GetTypeParameterList(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        return "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";
    }

    public static string GetConstraintClauses(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        foreach (var typeParameter in method.TypeParameters)
        {
            var constraints = new List<string>();
            if (typeParameter.HasReferenceTypeConstraint)
            {
                constraints.Add("class");
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
                constraints.Add(constraintType.ToDisplayString(s_qualifiedFormat));
            }

            if (typeParameter.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0)
            {
                clauses.Add($" where {typeParameter.Name} : {string.Join(", ", constraints)}");
            }
        }

        return string.Concat(clauses);
    }
}
