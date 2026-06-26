using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    public DotBoxDRpcJsonLowerer(
        SemanticModel model,
        ICollection<string> capabilities,
        ICollection<string> effects,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, RpcInlinedBinding>? inlinedBindings = null,
        IReadOnlyCollection<string>? inlineStack = null,
        List<string>? expressionPrelude = null,
        Func<string, string>? reserveGeneratedName = null,
        string? serverContextParameterName = null,
        ITypeSymbol? serverContextType = null)
    {
        _model = model;
        _capabilities = capabilities;
        _effects = effects;
        _cancellationToken = cancellationToken;
        _inlinedBindings = inlinedBindings;
        _inlineStack = inlineStack;
        _expressionPrelude = expressionPrelude;
        _reserveGeneratedName = reserveGeneratedName;
        _serverContextParameterName = serverContextParameterName;
        _serverContextType = serverContextType;
    }

    private string LowerRecordCreation(ObjectCreationExpressionSyntax creation)
    {
        var created = TypeOf(creation);
        if (DotBoxDRpcTypeMapper.ListElementType(created) is { } elementType &&
            creation.Initializer is null &&
            (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0))
        {
            Allocates = true;
            return Call("list.empty", DotBoxDRpcTypeMapper.JsonType(elementType));
        }
        if (TryLowerEmptyMapCreation(creation, created) is { } emptyMap)
        {
            return emptyMap;
        }
        if (created is not INamedTypeSymbol named || !DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            throw new NotSupportedException($"Server extension 'new {creation.Type}' must construct a supported DTO or empty list.");
        }
        Allocates = true;
        var fields = DotBoxDRpcTypeMapper.RecordFields(named);
        var args = new string[fields.Count];
        if (creation.ArgumentList is { Arguments.Count: > 0 } argumentList)
        {
            if (_model.GetSymbolInfo(creation, _cancellationToken).Symbol is not IMethodSymbol constructor ||
                argumentList.Arguments.Count != constructor.Parameters.Length ||
                constructor.Parameters.Length > fields.Count)
            {
                throw new NotSupportedException($"Server extension constructor for '{named.Name}' must pass one argument per constructor parameter, and the constructor must not have more parameters than the record has fields.");
            }
            var lowered = LowerArgumentsInParameterOrder(
                argumentList.Arguments,
                constructor.Parameters,
                $"Server extension constructor for '{named.Name}'");
            var assigned = new bool[fields.Count];
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var fieldIndex = ConstructorFieldIndex(fields, constructor.Parameters[i], named);
                if (assigned[fieldIndex])
                {
                    throw new NotSupportedException(
                        $"Server extension constructor for '{named.Name}' must map one argument per field.");
                }
                args[fieldIndex] = lowered[i];
                assigned[fieldIndex] = true;
            }
            if (creation.Initializer is { } initializer)
            {
                BindInitializer(initializer, fields, named, args, assigned, requireAllFields: false);
            }
            while (TryLowerDerivedField(fields, assigned, args, named))
            {
            }

            for (var i = 0; i < fields.Count; i++)
            {
                if (!assigned[i])
                {
                    args[i] = LowerDerivedField(fields, assigned, args, named, fields[i]);
                }
            }
        }
        else if (creation.Initializer is { } initializer)
        {
            BindInitializer(initializer, fields, named, args, assigned: null, requireAllFields: true);
        }
        else
        {
            throw new NotSupportedException($"Server extension 'new {named.Name}' must use constructor arguments or an object initializer.");
        }
        return Call("record.new", DotBoxDRpcTypeMapper.JsonType(named), args);
    }

    private void BindInitializer(
        InitializerExpressionSyntax initializer,
        IReadOnlyList<RecordMember> fields,
        INamedTypeSymbol named,
        string[] args,
        bool[]? assigned,
        bool requireAllFields)
    {
        assigned ??= new bool[fields.Count];
        foreach (var entry in initializer.Expressions)
        {
            if (entry is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax fieldName } assignment)
            {
                throw new NotSupportedException($"Server extension initializer for '{named.Name}' must assign named fields.");
            }
            var index = IndexOfField(fields, fieldName.Identifier.ValueText, named);
            args[index] = LowerExpression(assignment.Right);
            assigned[index] = true;
        }
        if (!requireAllFields)
        {
            return;
        }

        while (TryLowerDerivedField(fields, assigned, args, named))
        {
        }

        for (var i = 0; i < assigned.Length; i++)
        {
            if (!assigned[i])
            {
                throw new NotSupportedException($"Server extension initializer for '{named.Name}' must set field '{fields[i].Name}'.");
            }
        }
    }

    private static int IndexOfField(IReadOnlyList<RecordMember> fields, string name, INamedTypeSymbol named)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new NotSupportedException($"Server extension '{named.Name}' has no field '{name}'.");
    }

    private static int ConstructorFieldIndex(
        IReadOnlyList<RecordMember> fields,
        IParameterSymbol parameter,
        INamedTypeSymbol named)
    {
        var index = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
        if (index >= 0)
        {
            return index;
        }

        throw new NotSupportedException(
            $"Server extension DTO '{named.Name}' must expose a constructor matching its public fields.");
    }
}
