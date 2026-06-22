using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Helpers = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.Helpers;
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

        // Object-initializer construction (e.g. `new() { Success = true, Damage = x }`) is how the generated
        // hook-result builders and explicit `new Result { ... }` produce a value: lower each assigned field and
        // fill the omitted ones with their manifest-tag zero. Mixed positional + initializer is rejected.
        if (creation.Initializer is { } initializer)
        {
            if (arguments.Count > 0)
            {
                throw new System.NotSupportedException();
            }

            return LowerInitializer(fields, recordTypeSource, initializer, context, lowerExpression);
        }

        if (arguments.Count != fields.Count || constructor.Parameters.Length != fields.Count)
        {
            // Only a full positional construction (one argument per field) lowers; partial positional
            // constructions are not expressible as a single record.new.
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

            var lowered = LowerFieldValue(
                arguments[argumentIndex].Expression,
                fields[fieldIndex].Type,
                context,
                lowerExpression);

            fieldSources[fieldIndex] = lowered.Source;
            allocates |= lowered.Allocates;
        }

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);

        return new DotBoxDExpressionModel(RecordNew(fieldSources, recordTypeSource), ManifestTypes.Record, allocates);
    }

    // The record.new IR-construction source for the given (declaration-ordered) field sources. Shared by the
    // positional, object-initializer, and fluent result-builder lowering paths.
    internal static string RecordNew(IReadOnlyList<string> fieldSources, string recordTypeSource)
        => $"new {TypeNames.GlobalCallExpression}(\"record.new\", " +
            $"[{string.Join(", ", fieldSources)}], {recordTypeSource}, Span)";

    // Lowers an object-initializer construction to record.new: each `Field = value` assignment lowers the value
    // with the field's expected type. When Roslyn has a real RHS type, require an exact CLR match; generated
    // remote fallback chains can have unresolved lambda parameters on the first pass, so those rely on the
    // contextual lowerer's manifest-tag check. Omitted fields are filled with their manifest-tag zero.
    private static DotBoxDExpressionModel LowerInitializer(
        IReadOnlyList<RecordMember> fields,
        string recordTypeSource,
        InitializerExpressionSyntax initializer,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var fieldSources = new string?[fields.Count];
        var allocates = true;
        foreach (var expression in initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assignment ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                assignment.Left is not IdentifierNameSyntax fieldName)
            {
                throw new System.NotSupportedException();
            }

            var index = FieldIndex(fields, fieldName.Identifier.ValueText);
            if (index < 0 || fieldSources[index] is not null)
            {
                throw new System.NotSupportedException();
            }

            var lowered = LowerFieldValue(assignment.Right, fields[index].Type, context, lowerExpression);

            fieldSources[index] = lowered.Source;
            allocates |= lowered.Allocates;
        }

        for (var i = 0; i < fields.Count; i++)
        {
            fieldSources[i] ??= ZeroSource(fields[i].Type);
        }

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);

        return new DotBoxDExpressionModel(
            RecordNew(System.Array.ConvertAll(fieldSources, static s => s!), recordTypeSource),
            ManifestTypes.Record,
            allocates);
    }

    // The manifest-tag zero literal for an omitted field. Only scalar fields can be defaulted; a non-scalar
    // (Guid/list/map/record) omission fails safe because there is no single-expression zero for it.
    internal static string ZeroSource(ITypeSymbol fieldType)
    {
        if (DotBoxDNullableScalarType.IsNullableValueType(fieldType))
        {
            return DotBoxDNullableScalarExpressionLowerer.NullSource(fieldType);
        }

        return NonNullableZeroSource(fieldType);
    }

    private static DotBoxDExpressionModel LowerFieldValue(
        ExpressionSyntax expression,
        ITypeSymbol fieldType,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (DotBoxDNullableScalarExpressionLowerer.TryLower(
                expression,
                fieldType,
                context,
                lowerExpression,
                out var nullable))
        {
            return nullable;
        }

        var lowered = lowerExpression(expression);
        var rhsType = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (rhsType is { TypeKind: not TypeKind.Error } &&
            !SymbolEqualityComparer.Default.Equals(rhsType, fieldType))
        {
            throw new System.NotSupportedException();
        }

        if (!string.Equals(lowered.Type, SandboxTypeSourceEmitter.ManifestTag(fieldType), StringComparison.Ordinal))
        {
            throw new System.NotSupportedException();
        }

        return lowered;
    }

    private static string NonNullableZeroSource(ITypeSymbol fieldType)
        => SandboxTypeSourceEmitter.ManifestTag(fieldType) switch
        {
            ManifestTypes.Bool => $"{Helpers.Bool}({DotBoxDGenerationNames.CSharpLiterals.False})",
            ManifestTypes.Int => $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
            ManifestTypes.Long => $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
            ManifestTypes.Double => $"{Helpers.F64}({DotBoxDGenerationNames.CSharpLiterals.DoubleDefault})",
            ManifestTypes.String => $"{Helpers.Str}({DotBoxDGenerationNames.CSharpLiterals.StringDefault})",
            _ => throw new System.NotSupportedException(),
        };

    private static int FieldIndex(IReadOnlyList<RecordMember> fields, string name)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
