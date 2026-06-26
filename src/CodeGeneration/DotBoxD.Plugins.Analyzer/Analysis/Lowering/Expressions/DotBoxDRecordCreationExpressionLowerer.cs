using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
/// type and the runtime marshaller uses to reconstruct it), so the round-trip preserves field identity.
/// </summary>
internal static partial class DotBoxDRecordCreationExpressionLowerer
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
        // fill the omitted ones with their manifest-tag zero. If constructor arguments are present, lower them
        // first and let the initializer override them, matching C# object-initializer semantics.
        if (creation.Initializer is { } initializer)
        {
            if (arguments.Count > 0)
            {
                var fieldSources = new string?[fields.Count];
                var assigned = new bool[fields.Count];
                var allocates = LowerConstructorArguments(
                    constructor,
                    arguments,
                    fields,
                    fieldSources,
                    assigned,
                    context,
                    lowerExpression);
                return LowerInitializer(
                    fields,
                    recordTypeSource,
                    initializer,
                    context,
                    lowerExpression,
                    fieldSources,
                    assigned,
                    allocates);
            }

            return LowerInitializer(
                fields,
                recordTypeSource,
                initializer,
                context,
                lowerExpression,
                fieldSources: null,
                assigned: null,
                allocates: true);
        }

        if (arguments.Count != constructor.Parameters.Length || constructor.Parameters.Length > fields.Count)
        {
            // Constructor arguments must bind one argument per declared constructor parameter, and the constructor
            // cannot expose more fields than the DTO has. Missing DTO fields are filled below from derived getters
            // or manifest zeros where that is faithful to an omitted stored field.
            throw new System.NotSupportedException();
        }

        // record.new wants its arguments in the DTO's declared field order. Map each declared field to the
        // constructor argument that fills it (positional records line up 1:1; named/reordered constructors are
        // resolved by parameter name) and lower that argument with the field's expected type.
        var positionalSources = new string?[fields.Count];
        var positionalAssigned = new bool[fields.Count];
        var positionalAllocates = LowerConstructorArguments(
            constructor,
            arguments,
            fields,
            positionalSources,
            positionalAssigned,
            context,
            lowerExpression);
        positionalAllocates = FillOmittedFields(
            fields,
            positionalSources,
            positionalAssigned,
            context,
            allowStoredZero: true,
            allocates: positionalAllocates);

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);

        return new DotBoxDExpressionModel(
            RecordNew(System.Array.ConvertAll(positionalSources, static s => s!), recordTypeSource),
            ManifestTypes.Record,
            positionalAllocates);
    }

    private static bool LowerConstructorArguments(
        IMethodSymbol constructor,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<RecordMember> fields,
        string?[] fieldSources,
        bool[] assigned,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (arguments.Count != constructor.Parameters.Length || constructor.Parameters.Length > fields.Count)
        {
            throw new System.NotSupportedException();
        }

        var allocates = true;
        var assignedParameters = new bool[constructor.Parameters.Length];
        for (var i = 0; i < arguments.Count; i++)
        {
            var parameterIndex = ParameterIndex(constructor, arguments[i], i);
            if (parameterIndex < 0 || assignedParameters[parameterIndex])
            {
                throw new System.NotSupportedException();
            }

            assignedParameters[parameterIndex] = true;
            var parameter = constructor.Parameters[parameterIndex];
            var fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
            if (fieldIndex < 0 || assigned[fieldIndex])
            {
                throw new System.NotSupportedException();
            }

            // record.new declares the FIELD's sandbox type, and the value comes from the constructor parameter that
            // fills it. If that parameter is a different CLR type than the field (a converting ctor, e.g. an int
            // arg into an enum field, or List<long> into a List<int> field), the coarse manifest-tag check below
            // would still pass while the emitted record carried a value of the wrong shape. Require an exact type
            // match so only a faithful positional construction lowers; anything else fails safe.
            var lowered = LowerFieldValue(
                arguments[i].Expression,
                fields[fieldIndex].Type,
                context,
                lowerExpression);

            fieldSources[fieldIndex] = lowered.Source;
            assigned[fieldIndex] = true;
            allocates |= lowered.Allocates;
        }

        return allocates;
    }

    private static int ParameterIndex(IMethodSymbol constructor, ArgumentSyntax argument, int ordinal)
    {
        if (argument.NameColon is null)
        {
            return ordinal < constructor.Parameters.Length ? ordinal : -1;
        }

        var name = argument.NameColon.Name.Identifier.ValueText;
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            if (string.Equals(constructor.Parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        string?[]? fieldSources,
        bool[]? assigned,
        bool allocates)
    {
        fieldSources ??= new string?[fields.Count];
        assigned ??= new bool[fields.Count];
        foreach (var expression in initializer.Expressions)
        {
            if (expression is not AssignmentExpressionSyntax assignment ||
                assignment.Kind() != SyntaxKind.SimpleAssignmentExpression ||
                assignment.Left is not IdentifierNameSyntax fieldName)
            {
                throw new System.NotSupportedException();
            }

            var index = FieldIndex(fields, fieldName.Identifier.ValueText);
            if (index < 0)
            {
                throw new System.NotSupportedException();
            }

            var lowered = LowerFieldValue(assignment.Right, fields[index].Type, context, lowerExpression);

            fieldSources[index] = lowered.Source;
            assigned[index] = true;
            allocates |= lowered.Allocates;
        }

        allocates = FillOmittedFields(
            fields,
            fieldSources,
            assigned,
            context,
            allowStoredZero: true,
            allocates: allocates);

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);

        return new DotBoxDExpressionModel(
            RecordNew(System.Array.ConvertAll(fieldSources, static s => s!), recordTypeSource),
            ManifestTypes.Record,
            allocates);
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
}
