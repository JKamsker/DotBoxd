namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// DTO marshalling for <see cref="RpcKernelValueConversionEmitter"/>: a DTO is written as a positional
/// <c>Record</c> of its public instance properties (declaration order) and read back through a constructor
/// whose parameters match those fields by name and type. All field expressions are computed before the
/// owning helper method is appended, so nested list/DTO helpers never interleave with the body being built.
/// </summary>
internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureDtoWriter(INamedTypeSymbol type)
    {
        var key = TypeKey(type);
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        var fieldExpressions = DtoWriteExpressions(type);
        _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[]");
        _helpers.AppendLine("        {");
        foreach (var fieldExpression in fieldExpressions)
        {
            _helpers.Append("            ").Append(fieldExpression).AppendLine(",");
        }

        _helpers.AppendLine("        });");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureDtoReader(INamedTypeSymbol type)
    {
        var key = TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);

        // Compute the field expressions (which append nested list/DTO helpers) BEFORE writing this method's
        // body, so a nested helper is never spliced into the middle of the reconstruction statement.
        var body = BuildDtoReconstruction(type, fields);

        _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        _helpers.Append("        if (value.ItemCount != ").Append(fields.Count).AppendLine(")");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated DTO shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine(body);
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    /// <summary>
    /// Reconstructs a DTO from its positional <c>__fields</c>: through a constructor matching the public
    /// fields when one exists, otherwise through an object initializer (parameterless constructor + settable
    /// properties). Throws at generation time when neither shape is available.
    /// </summary>
    private string BuildDtoReconstruction(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        if (TryResolveConstructor(type, fields) is { } constructor)
        {
            var construction = "new " + TypeName(type) + "(" +
                string.Join(", ", DtoConstructorArguments(fields, constructor.Symbol)) + ")";
            if (!HasWritableUnassignedField(fields, constructor.Assigned))
            {
                return "        return " + construction + ";";
            }

            return BuildDtoInitializer("        return " + construction, fields, constructor.Assigned);
        }

        if (CanUseObjectInitializer(type, fields))
        {
            return BuildDtoInitializer("        return new " + TypeName(type), fields, assigned: null);
        }

        throw new NotSupportedException(
            $"Server extension DTO '{type.ToDisplayString()}' must expose either a constructor matching its " +
            "public fields or a parameterless constructor with settable properties.");
    }

    private List<string> DtoWriteExpressions(INamedTypeSymbol type)
    {
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);
        var expressions = new List<string>(fields.Count);
        foreach (var field in fields)
        {
            expressions.Add(WriteExpression(field.Type, "value." + Identifier(field.Name)));
        }

        return expressions;
    }

    private string BuildDtoInitializer(string construction, IReadOnlyList<RecordMember> fields, bool[]? assigned)
    {
        var initializer = new StringBuilder();
        initializer.Append(construction).AppendLine();
        initializer.AppendLine("        {");
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null && (assigned[i] || !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i])))
            {
                continue;
            }

            initializer.Append("            ").Append(Identifier(fields[i].Name)).Append(" = ")
                .Append(ReadExpression(fields[i].Type, "value.GetItem(" + i + ")")).AppendLine(",");
        }

        initializer.Append("        };");
        return initializer.ToString();
    }

    private List<string> DtoConstructorArguments(
        IReadOnlyList<RecordMember> fields,
        IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            arguments.Add(ReadExpression(fields[fieldIndex].Type, "value.GetItem(" + fieldIndex + ")"));
        }

        return arguments;
    }

    private static ResolvedDtoConstructor? TryResolveConstructor(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        ResolvedDtoConstructor? partial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility is not (
                    Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) ||
                constructor.Parameters.Length > fields.Count ||
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
                var assignedCount = AssignedCount(assigned);
                var resolved = new ResolvedDtoConstructor(constructor, assigned, assignedCount);
                if (assignedCount == fields.Count)
                {
                    return resolved;
                }

                if (partial is null || assignedCount > partial.AssignedCount)
                {
                    partial = resolved;
                }
            }
        }

        return partial;
    }

    /// <summary>
    /// A DTO can be reconstructed with an object initializer when every field has an accessible setter
    /// (<c>set</c> or <c>init</c>) and the type is a value type or exposes an accessible parameterless
    /// constructor — the same fallback the runtime marshaller uses.
    /// </summary>
    private static bool CanUseObjectInitializer(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        if (!type.IsValueType && !HasAccessibleParameterlessConstructor(type))
        {
            return false;
        }

        foreach (var field in fields)
        {
            if (!DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility is
                    Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasWritableUnassignedField(IReadOnlyList<RecordMember> fields, bool[] assigned)
        => fields.Where((_, index) => !assigned[index]).Any(DotBoxDRpcTypeMapper.IsObjectInitializerWritable);

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
}
