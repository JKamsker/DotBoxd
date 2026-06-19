namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

internal static class RpcKernelPayloadDtoReaderBuilder
{
    public static string BuildReconstruction(INamedTypeSymbol type, IReadOnlyList<IPropertySymbol> fields)
    {
        if (TryResolveConstructor(type, fields) is { } constructor)
        {
            return "        return new " + TypeName(type) + "(" +
                string.Join(", ", DtoConstructorArguments(fields, constructor)) + ");";
        }

        if (CanUseObjectInitializer(type, fields))
        {
            var initializer = new StringBuilder();
            initializer.Append("        return new ").Append(TypeName(type)).AppendLine();
            initializer.AppendLine("        {");
            for (var i = 0; i < fields.Count; i++)
            {
                initializer.Append("            ").Append(Identifier(fields[i].Name)).Append(" = ")
                    .Append(FieldLocal(i)).AppendLine(",");
            }

            initializer.Append("        };");
            return initializer.ToString();
        }

        throw new NotSupportedException(
            $"Server extension DTO '{type.ToDisplayString()}' must expose either a constructor matching its " +
            "public fields or a parameterless constructor with settable properties.");
    }

    private static List<string> DtoConstructorArguments(
        IReadOnlyList<IPropertySymbol> fields,
        IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            arguments.Add(FieldLocal(fieldIndex));
        }

        return arguments;
    }

    private static IMethodSymbol? TryResolveConstructor(INamedTypeSymbol type, IReadOnlyList<IPropertySymbol> fields)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility is not (
                    Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) ||
                constructor.Parameters.Length != fields.Count ||
                constructor.Parameters.Length == 0)
            {
                continue;
            }

            var matched = true;
            var assigned = new bool[fields.Count];
            foreach (var parameter in constructor.Parameters)
            {
                var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
                if (fieldIndex < 0 || assigned[fieldIndex])
                {
                    matched = false;
                    break;
                }

                assigned[fieldIndex] = true;
            }

            if (matched)
            {
                return constructor;
            }
        }

        return null;
    }

    private static bool CanUseObjectInitializer(INamedTypeSymbol type, IReadOnlyList<IPropertySymbol> fields)
    {
        if (fields.Count == 0 || (!type.IsValueType && !HasAccessibleParameterlessConstructor(type)))
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (field.SetMethod is not
                {
                    DeclaredAccessibility: Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                })
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0 &&
            constructor.DeclaredAccessibility is
                Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal);

    private static string FieldLocal(int index)
        => "__field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;
}
