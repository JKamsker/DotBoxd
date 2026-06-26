using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Helpers = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.Helpers;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDRecordCreationExpressionLowerer
{
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

    private static bool FillOmittedFields(
        IReadOnlyList<RecordMember> fields,
        string?[] fieldSources,
        bool[] assigned,
        DotBoxDExpressionLoweringContext context,
        bool allowStoredZero,
        bool allocates)
    {
        while (TryFillDerivedField(fields, fieldSources, assigned, context, ref allocates))
        {
        }

        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                continue;
            }

            if (!allowStoredZero || IsComputedProperty(fields[i]))
            {
                throw new NotSupportedException();
            }

            fieldSources[i] = ZeroSource(fields[i].Type);
            assigned[i] = true;
        }

        return allocates;
    }

    private static bool TryFillDerivedField(
        IReadOnlyList<RecordMember> fields,
        string?[] fieldSources,
        bool[] assigned,
        DotBoxDExpressionLoweringContext context,
        ref bool allocates)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i] ||
                !DotBoxDRpcTypeMapper.IsDerivedFromAssignedFields(fields[i], fields, assigned))
            {
                continue;
            }

            var lowered = LowerDerivedField(fields, fieldSources, assigned, context, fields[i]);
            fieldSources[i] = lowered.Source;
            assigned[i] = true;
            allocates |= lowered.Allocates;
            return true;
        }

        return false;
    }

    private static DotBoxDExpressionModel LowerDerivedField(
        IReadOnlyList<RecordMember> fields,
        string?[] fieldSources,
        bool[] assigned,
        DotBoxDExpressionLoweringContext context,
        RecordMember field)
    {
        if (field.Symbol is not IPropertySymbol property ||
            DotBoxDRpcTypeMapper.TryGetDerivedGetterExpression(property) is not { } body)
        {
            throw new NotSupportedException();
        }

        var bindings = new Dictionary<string, DotBoxDExpressionModel>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (!assigned[i] || fieldSources[i] is not { } source)
            {
                continue;
            }

            bindings[fields[i].Name] = new DotBoxDExpressionModel(
                source,
                SandboxTypeSourceEmitter.ManifestTag(fields[i].Type),
                false);
        }

        var bodyModel = context.SemanticModel.Compilation.GetSemanticModel(body.SyntaxTree);
        var bodyContext = new DotBoxDExpressionLoweringContext(
            eventParameterName: string.Empty,
            eventProperties: default,
            liveSettings: default,
            bodyModel,
            context.CancellationToken,
            inlinedBindings: bindings);
        var lowered = DotBoxDExpressionModelFactory.Create(body, bodyContext);
        if (!string.Equals(lowered.Type, SandboxTypeSourceEmitter.ManifestTag(field.Type), StringComparison.Ordinal))
        {
            throw new NotSupportedException();
        }

        return lowered;
    }

    private static bool IsComputedProperty(RecordMember field)
        => field.Symbol is IPropertySymbol { SetMethod: null };

    private static string NonNullableZeroSource(ITypeSymbol fieldType)
        => SandboxTypeSourceEmitter.ManifestTag(fieldType) switch
        {
            ManifestTypes.Bool => $"{Helpers.Bool}({DotBoxDGenerationNames.CSharpLiterals.False})",
            ManifestTypes.Int => $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
            ManifestTypes.Long => $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
            ManifestTypes.Double => $"{Helpers.F64}({DotBoxDGenerationNames.CSharpLiterals.DoubleDefault})",
            ManifestTypes.String => $"{Helpers.Str}({DotBoxDGenerationNames.CSharpLiterals.StringDefault})",
            _ => throw new NotSupportedException(),
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
}
