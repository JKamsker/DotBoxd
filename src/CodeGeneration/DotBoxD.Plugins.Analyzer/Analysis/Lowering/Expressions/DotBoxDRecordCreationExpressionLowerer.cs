using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Lowers a record/DTO construction in a remote <c>Select</c> projection — e.g.
/// <c>Select(e =&gt; new SkillHit(e.Id, e.CasterLevel, e.Accepted))</c> — into the sandbox <c>record.new</c>
/// intrinsic, so a handler can build a rich projected DTO server-side and have it pushed in one IPC frame
/// rather than calling back into the server per field. The constructed value's positional fields are emitted in
/// the DTO's declared field order (the same order <see cref="SandboxTypeSourceEmitter"/> uses for the record
/// type and the runtime marshaller uses to reconstruct it), so the round-trip preserves field identity. Only a
/// positional construction of a marshaller-eligible DTO lowers; anything else fails safe.
/// </summary>
internal static class DotBoxDRecordCreationExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        BaseObjectCreationExpressionSyntax creation,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor ||
            constructor.MethodKind != MethodKind.Constructor ||
            constructor.ContainingType is not INamedTypeSymbol recordType ||
            !DotBoxDRpcTypeMapper.IsRecordDto(recordType))
        {
            return null;
        }

        if (SandboxTypeSourceEmitter.TryEmit(recordType) is not { } recordTypeSource)
        {
            throw new System.NotSupportedException();
        }

        var fields = DotBoxDRpcTypeMapper.RecordFields(recordType);
        var arguments = creation.ArgumentList?.Arguments ?? default;
        if (arguments.Count != fields.Count || constructor.Parameters.Length != fields.Count)
        {
            // Only a full positional construction (one argument per field) lowers; object initializers and
            // partial constructions are not expressible as a single record.new.
            throw new System.NotSupportedException();
        }

        // record.new wants its arguments in the DTO's declared field order. Map each declared field to the
        // constructor argument that fills it (positional records line up 1:1; named/reordered constructors are
        // resolved by parameter name) and lower that argument with the field's expected type.
        var fieldSources = new string[fields.Count];
        var allocates = true;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var argumentIndex = ConstructorArgumentIndex(constructor, arguments, fields[fieldIndex].Name);
            if (argumentIndex < 0)
            {
                throw new System.NotSupportedException();
            }

            // record.new declares the FIELD's sandbox type, and the value comes from the constructor parameter that
            // fills it. If that parameter is a different CLR type than the field (a converting ctor, e.g. an int
            // arg into an enum field, or List<long> into a List<int> field), the coarse manifest-tag check below
            // would still pass while the emitted record carried a value of the wrong shape. Require an exact type
            // match so only a faithful positional construction lowers; anything else fails safe.
            if (!SymbolEqualityComparer.Default.Equals(constructor.Parameters[argumentIndex].Type, fields[fieldIndex].Type))
            {
                throw new System.NotSupportedException();
            }

            var lowered = lowerExpression(arguments[argumentIndex].Expression);
            if (!string.Equals(lowered.Type, SandboxTypeSourceEmitter.ManifestTag(fields[fieldIndex].Type), StringComparison.Ordinal))
            {
                throw new System.NotSupportedException();
            }

            fieldSources[fieldIndex] = lowered.Source;
            allocates |= lowered.Allocates;
        }

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);

        var source =
            $"new {TypeNames.GlobalCallExpression}(\"record.new\", " +
            $"[{string.Join(", ", fieldSources)}], {recordTypeSource}, Span)";
        return new DotBoxDExpressionModel(source, ManifestTypes.Record, allocates);
    }

    // The positional argument that fills the field named <paramref name="fieldName"/>: the argument whose
    // constructor parameter matches the field by name (exact first, then a single case-insensitive match),
    // mirroring the runtime marshaller's field-to-constructor mapping. Named arguments are not accepted —
    // a remote Select projects with positional construction.
    private static int ConstructorArgumentIndex(
        IMethodSymbol constructor,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string fieldName)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon is not null)
            {
                return -1;
            }
        }

        var match = -1;
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            if (string.Equals(constructor.Parameters[i].Name, fieldName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            if (!string.Equals(constructor.Parameters[i].Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match >= 0)
            {
                return -1;
            }

            match = i;
        }

        return match;
    }
}
