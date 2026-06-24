using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Member-access lowering for <see cref="DotBoxDExpressionModelFactory"/>: reading a field of a projected record
/// (<c>record.get</c>) and the general member chain — a <c>.Count</c>/<c>.Length</c> read on a list value
/// (<c>list.count</c>) or a named field on a record value, where the receiver may be a projected element, an event
/// property, a host-call result, or a further hop.
/// </summary>
internal static partial class DotBoxDExpressionModelFactory
{
    private static DotBoxDExpressionModel? TryLowerContextMember(
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.ServerContextParameterName is null ||
            member.Expression is not IdentifierNameSyntax receiver ||
            !string.Equals(receiver.Identifier.ValueText, context.ServerContextParameterName, StringComparison.Ordinal))
        {
            return null;
        }

        return context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol switch
        {
            IPropertySymbol property when HasLocalAttribute(property) => throw new NotSupportedException(
                "[Local] context members cannot be used in lowered server-side IR."),
            IPropertySymbol property => DotBoxDHostBindingExpressionLowerer.TryLowerProperty(property, context)
                ?? throw new NotSupportedException(
                    $"Unsupported server context property '{memberName}'. Mark sandbox-readable context properties with [HostBinding]."),
            _ => null
        };
    }

    // Reads a field of the projected record (after a Select) as record.get(projection, index). The field is
    // matched by name against the projected DTO's declared fields — the same positional order record.new emitted
    // and the kernel parameter / decoder use — so dto.Field crosses to exactly that field's value and type.
    // Returns null when the projection is not a record or has no such field, so the caller can try the general
    // member chain; it is never reinterpreted as an event property.
    private static DotBoxDExpressionModel? TryLowerProjectedRecordField(
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.ProjectedElement is { } projected &&
            context.ProjectedElementType is INamedTypeSymbol recordType &&
            IsRecordShaped(recordType))
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(recordType);
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Name, memberName, StringComparison.Ordinal))
                {
                    continue;
                }

                return RecordGet(projected, i, fields[i].Type, allocates: false);
            }
        }

        return null;
    }

    // The general member chain: lower the receiver expression, then read `.Count`/`.Length` off a list value
    // (list.count -> i32) or a named field off a record value (record.get -> field type). The receiver's CLR
    // type drives which read applies; the lowered receiver's sandbox shape is double-checked so a mismatch
    // fails safe rather than emitting an invalid intrinsic.
    private static DotBoxDExpressionModel? TryLowerMemberChain(
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        var receiverType = ResolveType(member.Expression, context);
        if (receiverType is null)
        {
            return null;
        }

        if ((string.Equals(memberName, "Count", StringComparison.Ordinal) ||
             string.Equals(memberName, "Length", StringComparison.Ordinal)) &&
            IsListShaped(receiverType))
        {
            var receiver = Lower(member.Expression, context);
            if (!string.Equals(receiver.Type, DotBoxDGenerationNames.ManifestTypes.List, StringComparison.Ordinal))
            {
                return null;
            }

            var source =
                $"new {DotBoxDGenerationNames.TypeNames.GlobalCallExpression}(" +
                $"{LiteralReader.StringLiteral("list.count")}, [{receiver.Source}], null, Span)";
            return new DotBoxDExpressionModel(source, DotBoxDGenerationNames.ManifestTypes.Int, receiver.Allocates);
        }

        if (receiverType is INamedTypeSymbol named && IsRecordShaped(named))
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(named);
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Name, memberName, StringComparison.Ordinal))
                {
                    continue;
                }

                var receiver = Lower(member.Expression, context);
                if (!string.Equals(receiver.Type, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal))
                {
                    return null;
                }

                return RecordGet(receiver, i, fields[i].Type, receiver.Allocates);
            }
        }

        return null;
    }

    // A type is "record-shaped" only when its marshaller manifest tag is Record — i.e. it is a wire-eligible DTO
    // and NOT a list/map/scalar. This mirrors SandboxTypeSourceEmitter's list-before-record dispatch and avoids
    // the trap where IsRecordDto alone is true for a List/collection (which exposes public Count/Capacity
    // properties) and a field read would be emitted as record.get on a List value.
    private static bool IsRecordShaped(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           DotBoxDRpcTypeMapper.IsRecordDto(named) &&
           string.Equals(
            SandboxTypeSourceEmitter.ManifestTag(type),
            DotBoxDGenerationNames.ManifestTypes.Record,
            StringComparison.Ordinal);

    private static DotBoxDExpressionModel RecordGet(
        DotBoxDExpressionModel record,
        int index,
        ITypeSymbol fieldType,
        bool allocates)
    {
        var source =
            $"new {DotBoxDGenerationNames.TypeNames.GlobalCallExpression}(" +
            $"{LiteralReader.StringLiteral("record.get")}, " +
            $"[{record.Source}, {DotBoxDGenerationNames.Helpers.I32}({index})], null, Span)";
        return new DotBoxDExpressionModel(source, SandboxTypeSourceEmitter.ManifestTag(fieldType), allocates);
    }

    private static ITypeSymbol? ResolveType(ExpressionSyntax expression, DotBoxDExpressionLoweringContext context)
    {
        var info = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return info.Type ?? info.ConvertedType;
    }

    private static bool IsListShaped(ITypeSymbol type)
    {
        try
        {
            return DotBoxDRpcTypeMapper.ListElementType(type) is not null;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool HasLocalAttribute(IPropertySymbol property)
        => property.GetAttributes().Any(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            DotBoxDMetadataNames.LocalAttribute,
            StringComparison.Ordinal));

    /// <summary>
    /// Records the capability gating a <c>[Capability]</c>-annotated event property so reading it
    /// contributes to the kernel's required capabilities (deny-at-install if the policy lacks it).
    /// Unannotated properties stay ungated.
    /// </summary>
    private static void CollectEventPropertyCapability(
        MemberAccessExpressionSyntax member,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.Capabilities is null ||
            context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol is not IPropertySymbol property)
        {
            return;
        }

        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.CapabilityAttribute,
                    StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string id &&
                !string.IsNullOrEmpty(id))
            {
                context.Capabilities.Add(id);
            }
        }
    }
}
