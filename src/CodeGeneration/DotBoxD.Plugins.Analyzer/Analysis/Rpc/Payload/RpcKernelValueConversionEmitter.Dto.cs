namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// DTO marshalling for <see cref="RpcKernelValueConversionEmitter"/>: a DTO is written as a positional
/// <c>Record</c> of its public readable properties and fields, then read back through a constructor
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
        _helpers.Append($"    private static {DotBoxDRpcValueNames.GlobalKernelRpcValue} ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(value);");
        _helpers.AppendLine($"        return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Record(new {DotBoxDRpcValueNames.GlobalKernelRpcValue}[]");
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
        var fieldReads = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            fieldReads[i] = ReadExpression(fields[i].Type, "value.GetItem(" + i + ")");
        }

        // Compute the field expressions (which append nested list/DTO helpers) BEFORE writing this method's
        // body, so a nested helper is never spliced into the middle of the reconstruction statement.
        var body = BuildDtoReconstruction(type, fields);

        _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine($"        value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.Record);");
        _helpers.Append("        if (value.ItemCount != ").Append(fields.Count).AppendLine(")");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated DTO shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        for (var i = 0; i < fields.Count; i++)
        {
            _helpers.Append("        var ").Append(FieldLocal(i)).Append(" = ")
                .Append(fieldReads[i]).AppendLine(";");
        }

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
            if (constructor.AssignedCount < fields.Count &&
                !DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, constructor.Assigned, _compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type.ToDisplayString()}' constructor '{constructor.Symbol.ToDisplayString()}' " +
                    "does not assign every public field and the remaining fields are not settable.");
            }

            ThrowIfRequiredReadOnlyMembersNeedSetsRequiredMembers(type, fields, constructor.Symbol);
            return BuildDtoInitializer(construction, fields, constructor.Assigned);
        }

        if (DotBoxDRpcTypeMapper.CanReconstructWithObjectInitializer(type, fields, _compilation))
        {
            return BuildDtoInitializer(
                "new " + TypeName(type),
                fields,
                assigned: new bool[fields.Count]);
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

    private string BuildDtoInitializer(
        string construction,
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned)
    {
        var initializer = new StringBuilder();
        var initialized = InitializerFieldIndexes(fields, assigned);
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

        AppendReadOnlyFieldVerifications(initializer, fields);
        initializer.AppendLine();
        initializer.Append("        return __result;");
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
            if (fieldIndex >= 0)
            {
                arguments.Add(Identifier(parameter.Name) + ": " + FieldLocal(fieldIndex));
                continue;
            }

            if (parameter.HasExplicitDefaultValue)
            {
                arguments.Add(RpcDtoFieldMatcher.DefaultConstructorArgument(parameter));
                continue;
            }

            throw new NotSupportedException(
                $"Server extension DTO '{constructor.ContainingType.ToDisplayString()}' constructor " +
                $"'{constructor.ToDisplayString()}' has a parameter that does not match a public field.");
        }

        return arguments;
    }

    private ResolvedDtoConstructor? TryResolveConstructor(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        ResolvedDtoConstructor? partial = null;
        ResolvedDtoConstructor? rejectedPartial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, _compilation) ||
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

                if (DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, assigned, _compilation))
                {
                    if (partial is null || assignedCount > partial.AssignedCount)
                    {
                        partial = resolved;
                    }
                }
                else if (rejectedPartial is null || assignedCount > rejectedPartial.AssignedCount)
                {
                    rejectedPartial = resolved;
                }
            }
        }

        return partial ?? rejectedPartial;
    }

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private void ThrowIfRequiredReadOnlyMembersNeedSetsRequiredMembers(
        INamedTypeSymbol type,
        IReadOnlyList<RecordMember> fields,
        IMethodSymbol constructor)
    {
        if (HasSetsRequiredMembers(constructor))
        {
            return;
        }

        foreach (var field in fields)
        {
            if (IsRequiredMember(field) &&
                !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, _compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type.ToDisplayString()}' required field '{field.Name}' is read-only; " +
                    "mark the constructor with SetsRequiredMembers or make the member settable.");
            }
        }
    }

    private static bool IsRequiredMember(RecordMember field)
        => field.Symbol switch
        {
            IPropertySymbol property => property.IsRequired,
            IFieldSymbol fieldSymbol => fieldSymbol.IsRequired,
            _ => false,
        };

    private static bool HasSetsRequiredMembers(IMethodSymbol constructor)
        => constructor.GetAttributes().Any(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
            StringComparison.Ordinal));

    private sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
}
