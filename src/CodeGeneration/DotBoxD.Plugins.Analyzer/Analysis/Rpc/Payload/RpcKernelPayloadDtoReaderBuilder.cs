namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

internal static class RpcKernelPayloadDtoReaderBuilder
{
    public static string BuildReconstruction(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation = null)
    {
        if (TryResolveConstructor(type, fields, compilation) is { } constructor)
        {
            var construction = "new " + TypeName(type) + "(" +
                string.Join(", ", DtoConstructorArguments(fields, constructor.Symbol)) + ")";
            if (constructor.AssignedCount < fields.Count &&
                !DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, constructor.Assigned, compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type.ToDisplayString()}' constructor '{constructor.Symbol.ToDisplayString()}' " +
                    "does not assign every public field and the remaining fields are not settable.");
            }

            return BuildInitializer(construction, fields, constructor.Assigned, compilation);
        }

        if (DotBoxDRpcTypeMapper.CanReconstructWithObjectInitializer(type, fields, compilation))
        {
            return BuildInitializer(
                "new " + TypeName(type),
                fields,
                assigned: new bool[fields.Count],
                compilation: compilation);
        }

        throw new NotSupportedException(
            $"Server extension DTO '{type.ToDisplayString()}' must expose either a constructor matching its " +
            "public fields or a parameterless constructor with settable properties.");
    }

    private static List<string> DtoConstructorArguments(
        IReadOnlyList<RecordMember> fields,
        IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            if (fieldIndex >= 0)
            {
                arguments.Add(Identifier(parameter.Name) + ": " + FieldLocal(fieldIndex));
                continue;
            }

            if (!parameter.HasExplicitDefaultValue)
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{constructor.ContainingType.ToDisplayString()}' constructor " +
                    $"'{constructor.ToDisplayString()}' has a parameter that does not match a public field.");
            }
        }

        return arguments;
    }

    private static string BuildInitializer(
        string construction,
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned,
        Compilation? compilation)
    {
        var initializer = new StringBuilder();
        var initialized = InitializerFieldIndexes(fields, assigned, compilation);
        initializer.Append("        var __result = ").Append(construction);
        if (initialized.Count == 0)
        {
            initializer.AppendLine(";");
        }
        else
        {
            initializer.AppendLine();
            initializer.AppendLine("        {");
            foreach (var i in initialized)
            {
                initializer.Append("            ").Append(Identifier(fields[i].Name)).Append(" = ")
                    .Append(FieldLocal(i)).AppendLine(",");
            }

            initializer.AppendLine("        };");
        }

        AppendReadOnlyFieldVerifications(initializer, fields, compilation);
        initializer.AppendLine();
        initializer.Append("        return __result;");
        return initializer.ToString();
    }

    private static List<int> InitializerFieldIndexes(
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned,
        Compilation? compilation)
    {
        var initialized = new List<int>();
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null &&
                !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i], compilation))
            {
                continue;
            }

            initialized.Add(i);
        }

        return initialized;
    }

    private static void AppendReadOnlyFieldVerifications(
        StringBuilder builder,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i], compilation))
            {
                continue;
            }

            if (!DotBoxDRpcTypeMapper.IsReadableFromGeneratedCode(fields[i], compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO field '{fields[i].Name}' is private or read-only and could not be reconstructed.");
            }

            builder.Append("        if (!global::System.Collections.Generic.EqualityComparer<")
                .Append(TypeName(fields[i].Type)).Append(">.Default.Equals(__result.")
                .Append(Identifier(fields[i].Name)).Append(", ")
                .Append(FieldLocal(i)).AppendLine("))");
            builder.AppendLine("        {");
            builder.Append("            throw new global::System.NotSupportedException(\"Server extension DTO field '")
                .Append(fields[i].Name)
                .AppendLine("' is private or read-only and could not be reconstructed.\");");
            builder.AppendLine("        }");
        }
    }

    private static ResolvedDtoConstructor? TryResolveConstructor(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        Compilation? compilation)
    {
        ResolvedDtoConstructor? partial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, compilation) ||
                constructor.Parameters.Length == 0)
            {
                continue;
            }

            var matched = true;
            var assigned = new bool[fields.Count];
            foreach (var parameter in constructor.Parameters)
            {
                var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
                if (fieldIndex < 0)
                {
                    if (parameter.HasExplicitDefaultValue)
                    {
                        continue;
                    }

                    matched = false;
                    break;
                }

                if (assigned[fieldIndex])
                {
                    matched = false;
                    break;
                }

                assigned[fieldIndex] = true;
            }

            if (matched)
            {
                RpcDtoFieldMatcher.ValidateNoRefLikeParameters(
                    constructor,
                    $"Server extension DTO '{type.ToDisplayString()}'");

                var assignedCount = AssignedCount(assigned);
                var resolved = new ResolvedDtoConstructor(constructor, assigned, assignedCount);
                if (assignedCount == fields.Count)
                {
                    return resolved;
                }

                if (DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, assigned, compilation) &&
                    (partial is null || assignedCount > partial.AssignedCount))
                {
                    partial = resolved;
                }
            }
        }

        return partial;
    }

    private static string FieldLocal(int index)
        => "__field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
}
