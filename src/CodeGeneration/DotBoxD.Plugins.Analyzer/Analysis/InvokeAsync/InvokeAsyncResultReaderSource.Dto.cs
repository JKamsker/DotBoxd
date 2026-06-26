using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncResultReaderSource
{
    private string BuildDtoReconstruction(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        if (TryResolveConstructor(type, fields) is { } constructor)
        {
            var construction = "new " + TypeName(type) + "(" +
                string.Join(", ", DtoConstructorArguments(fields, constructor.Symbol)) + ")";
            if (constructor.AssignedCount == fields.Count &&
                !RequiresRequiredMemberInitializer(fields, constructor.Symbol))
            {
                return "            return " + construction + ";";
            }

            if (!DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, constructor.Assigned, _compilation))
            {
                throw new NotSupportedException(
                    $"InvokeAsync DTO '{type.ToDisplayString()}' constructor '{constructor.Symbol.ToDisplayString()}' " +
                    "does not assign every public field and the remaining fields are not settable.");
            }

            return BuildDtoInitializer(construction, fields, constructor.Assigned, constructor.Symbol);
        }

        if (DotBoxDRpcTypeMapper.CanReconstructWithObjectInitializer(type, fields, _compilation))
        {
            return BuildDtoInitializer(
                "new " + TypeName(type),
                fields,
                assigned: new bool[fields.Count],
                constructor: null);
        }

        throw new NotSupportedException(
            $"InvokeAsync DTO '{type.ToDisplayString()}' must expose either a constructor matching its " +
            "public fields or a parameterless constructor with settable properties.");
    }

    private string BuildDtoInitializer(
        string construction,
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned,
        IMethodSymbol? constructor)
    {
        var initializer = new StringBuilder();
        var initialized = InitializerFieldIndexes(fields, assigned, constructor);
        initializer.Append("            var __result = ").Append(construction);
        if (initialized.Count == 0)
        {
            initializer.AppendLine(";");
        }
        else
        {
            initializer.AppendLine();
            initializer.AppendLine("            {");
            foreach (var i in initialized)
            {
                initializer.Append("                ").Append(Identifier(fields[i].Name)).Append(" = ")
                    .Append(FieldLocal(i)).AppendLine(",");
            }

            initializer.AppendLine("            };");
        }

        AppendReadOnlyFieldVerifications(initializer, fields, assigned);
        initializer.AppendLine();
        initializer.Append("            return __result;");
        return initializer.ToString();
    }

    private List<int> InitializerFieldIndexes(
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned,
        IMethodSymbol? constructor)
    {
        var initialized = new List<int>();
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null &&
                ((assigned[i] && !MustInitializeRequiredMember(fields[i], constructor)) ||
                 (!assigned[i] && !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i], _compilation))))
            {
                continue;
            }

            initialized.Add(i);
        }

        return initialized;
    }

    private void AppendReadOnlyFieldVerifications(
        StringBuilder builder,
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null &&
                (assigned[i] || DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i], _compilation)))
            {
                continue;
            }

            builder.Append("            if (!global::System.Collections.Generic.EqualityComparer<")
                .Append(TypeName(fields[i].Type)).Append(">.Default.Equals(__result.")
                .Append(Identifier(fields[i].Name)).Append(", ")
                .Append(FieldLocal(i)).AppendLine("))");
            builder.AppendLine("            {");
            builder.Append("                throw new global::System.NotSupportedException(\"InvokeAsync DTO field '")
                .Append(fields[i].Name)
                .AppendLine("' is private or read-only and could not be reconstructed.\");");
            builder.AppendLine("            }");
        }
    }

    private List<string> DtoConstructorArguments(IReadOnlyList<RecordMember> fields, IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            arguments.Add(FieldLocal(RpcDtoFieldMatcher.FieldIndex(fields, parameter)));
        }

        return arguments;
    }

    private ResolvedDtoConstructor? TryResolveConstructor(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        ResolvedDtoConstructor? partial = null;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!DotBoxDRpcTypeMapper.IsAccessibleFromGeneratedCode(constructor, _compilation) ||
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

                if (DotBoxDRpcTypeMapper.CanReconstructFromAssignedFields(fields, assigned, _compilation) &&
                    (partial is null || assignedCount > partial.AssignedCount))
                {
                    partial = resolved;
                }
            }
        }

        return partial;
    }

    private bool RequiresRequiredMemberInitializer(IReadOnlyList<RecordMember> fields, IMethodSymbol constructor)
        => !HasSetsRequiredMembers(constructor) &&
           fields.Any(field => IsRequiredMember(field) &&
                               DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, _compilation));

    private bool MustInitializeRequiredMember(RecordMember field, IMethodSymbol? constructor)
        => constructor is not null &&
           !HasSetsRequiredMembers(constructor) &&
           IsRequiredMember(field) &&
           DotBoxDRpcTypeMapper.IsObjectInitializerWritable(field, _compilation);

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

    private static string Identifier(string name)
        => "@" + name;

    private static int AssignedCount(bool[] assigned)
        => assigned.Count(static item => item);

    private sealed record ResolvedDtoConstructor(IMethodSymbol Symbol, bool[] Assigned, int AssignedCount);
}
