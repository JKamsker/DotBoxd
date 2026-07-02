using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpLiterals = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.CSharpLiterals;
using Helpers = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.Helpers;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Lowers a fluent hook-result builder chain — <c>Result.Ok().With&lt;Field&gt;(value)…</c> or
/// <c>Result.Reject(reason)</c> — directly to a single <c>record.new</c>. The generated <c>Ok</c>/<c>Reject</c>/
/// <c>With&lt;Field&gt;</c> members are emitted by the same generator pass, so their method symbols are NOT visible
/// while this chain is being lowered; recognition is therefore <b>syntactic</b> (by member name and arity),
/// resolving only the result <i>type</i> at the chain seed (which already exists). The chain is walked once and the field-source
/// array tracked structurally — the <c>Ok</c>/<c>Reject</c> seed sets <c>Success</c>/<c>Reason</c>, each
/// <c>With&lt;Field&gt;</c> overrides one slot, omitted slots take their manifest-tag zero — so the record is
/// materialised once with no quadratic <c>record.get</c> copying. Only a chain whose seed is a marshaller-eligible
/// <c>[HookResult]</c> record lowers; anything else returns null so the caller can try the next handler.
/// </summary>
internal static class DotBoxDResultBuilderExpressionLowerer
{
    private const string OkMethod = "Ok";
    private const string RejectMethod = "Reject";
    private const string WithPrefix = "With";
    private const string SuccessField = "Success";
    private const string ReasonField = "Reason";

    public static DotBoxDExpressionModel? TryLower(
        InvocationExpressionSyntax invocation,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (!IsBuilderInvocation(invocation) ||
            ResolveSeedResultType(invocation, context.SemanticModel, context.CancellationToken) is not { } resultType ||
            SandboxTypeSourceEmitter.TryEmit(resultType) is not { } recordTypeSource)
        {
            return null;
        }

        var fields = DotBoxDRpcTypeMapper.RecordFields(resultType);
        var sources = new string?[fields.Count];
        if (!TryApply(invocation, fields, sources, context, lowerExpression))
        {
            return null;
        }

        // Omitted fields take their manifest-tag zero. Deliberate, documented divergence for an omitted string
        // `Reason` (e.g. `Ok()` / `Reject()` with no argument): kernel IR strings are non-null, so the zero is the
        // empty string "", whereas the in-process generated builder (run by RegisterLocal / direct host calls)
        // leaves `Reason` at its C# default of null. `Reason` is only meaningful paired with `Success == false`,
        // which dispatch drops before returning, so the gap is unobservable in normal dispatch; a test pins the
        // convention so a future change that surfaces `Reason` cannot let the two transports silently diverge.
        for (var i = 0; i < fields.Count; i++)
        {
            sources[i] ??= DotBoxDRecordCreationExpressionLowerer.ZeroSource(fields[i].Type);
        }

        context.Effects?.Add(DotBoxDGenerationNames.Effects.Alloc);
        return new DotBoxDExpressionModel(
            DotBoxDRecordCreationExpressionLowerer.RecordNew(System.Array.ConvertAll(sources, static s => s!), recordTypeSource),
            ManifestTypes.Record,
            true);
    }

    private static bool IsBuilderInvocation(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member && IsBuilderName(member.Name.Identifier.ValueText);

    private static bool IsBuilderName(string name)
        => string.Equals(name, OkMethod, StringComparison.Ordinal)
            || string.Equals(name, RejectMethod, StringComparison.Ordinal)
            || (name.Length > WithPrefix.Length && name.StartsWith(WithPrefix, StringComparison.Ordinal));

    // Walks the chain to its Ok()/Reject() seed and resolves the type the seed is called on (e.g. the
    // `CombatDamageResult` in `CombatDamageResult.Ok()`), requiring it to carry [HookResult]. The seed's receiver
    // is a TYPE reference, which exists in the pre-generation compilation even though Ok/Reject themselves do not.
    internal static INamedTypeSymbol? ResolveSeedResultType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!IsBuilderInvocation(invocation))
        {
            return null;
        }

        var current = invocation;
        while (true)
        {
            if (current.Expression is not MemberAccessExpressionSyntax member)
            {
                return null;
            }

            var name = member.Name.Identifier.ValueText;
            if (string.Equals(name, OkMethod, StringComparison.Ordinal) || string.Equals(name, RejectMethod, StringComparison.Ordinal))
            {
                var resultType = semanticModel.GetSymbolInfo(member.Expression, cancellationToken).Symbol as INamedTypeSymbol;
                return resultType is not null &&
                    HasHookResultAttribute(resultType) &&
                    !UsesAuthorDefinedBuilderMember(invocation, resultType)
                    ? resultType
                    : null;
            }

            if (name.Length > WithPrefix.Length && name.StartsWith(WithPrefix, StringComparison.Ordinal) &&
                member.Expression is InvocationExpressionSyntax inner)
            {
                current = inner;
                continue;
            }

            return null;
        }
    }

    // Applies one builder call to the field-source array, recursing into the receiver for a With<Field> hop.
    private static bool TryApply(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<RecordMember> fields,
        string?[] sources,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var name = member.Name.Identifier.ValueText;
        var arguments = invocation.ArgumentList.Arguments;

        if (string.Equals(name, OkMethod, StringComparison.Ordinal) && arguments.Count == 0)
        {
            return SetByName(fields, sources, SuccessField, BoolSource(value: true));
        }

        if (string.Equals(name, RejectMethod, StringComparison.Ordinal) && arguments.Count <= 1)
        {
            if (!SetByName(fields, sources, SuccessField, BoolSource(value: false)))
            {
                return false;
            }

            if (arguments.Count != 1)
            {
                return true;
            }

            var reason = lowerExpression(arguments[0].Expression);
            return string.Equals(reason.Type, ManifestTypes.String, StringComparison.Ordinal)
                && SetByName(fields, sources, ReasonField, reason.Source);
        }

        if (name.Length > WithPrefix.Length && name.StartsWith(WithPrefix, StringComparison.Ordinal) && arguments.Count == 1)
        {
            var fieldName = name.Substring(WithPrefix.Length);
            if (string.Equals(fieldName, SuccessField, StringComparison.Ordinal) ||
                string.Equals(fieldName, ReasonField, StringComparison.Ordinal))
            {
                return false;
            }

            var index = FieldIndex(fields, fieldName);
            if (index < 0 ||
                member.Expression is not InvocationExpressionSyntax receiver ||
                !TryApply(receiver, fields, sources, context, lowerExpression))
            {
                return false;
            }

            var expectedType = SandboxTypeSourceEmitter.ManifestTag(fields[index].Type);
            var argument = DotBoxDNullableScalarExpressionLowerer.TryLower(
                arguments[0].Expression,
                fields[index].Type,
                context,
                lowerExpression,
                out var nullable)
                ? nullable
                : lowerExpression(arguments[0].Expression);
            if (!string.Equals(argument.Type, expectedType, StringComparison.Ordinal))
            {
                if (!DotBoxDGenerationNames.ManifestTypes.IsNumeric(expectedType) ||
                    DotBoxDNumericConstantPromoter.TryPromoteConstant(arguments[0].Expression, context, expectedType) is not { } promoted)
                {
                    return false;
                }

                argument = promoted;
            }

            sources[index] = argument.Source;
            return true;
        }

        return false;
    }

    private static bool UsesAuthorDefinedBuilderMember(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol resultType)
    {
        var current = invocation;
        while (current.Expression is MemberAccessExpressionSyntax member)
        {
            if (HasAuthorDefinedMember(
                    resultType,
                    member.Name.Identifier.ValueText,
                    current.ArgumentList.Arguments.Count))
            {
                return true;
            }

            if (member.Expression is not InvocationExpressionSyntax inner)
            {
                return false;
            }

            current = inner;
        }

        return false;
    }

    private static bool HasAuthorDefinedMember(INamedTypeSymbol resultType, string name, int parameterCount)
    {
        foreach (var member in resultType.GetMembers(name))
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                if (method.Parameters.Length == parameterCount)
                {
                    return true;
                }

                continue;
            }

            if (member is IPropertySymbol or IFieldSymbol or IEventSymbol)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SetByName(IReadOnlyList<RecordMember> fields, string?[] sources, string name, string source)
    {
        var index = FieldIndex(fields, name);
        if (index < 0)
        {
            return false;
        }

        sources[index] = source;
        return true;
    }

    private static string BoolSource(bool value)
        => $"{Helpers.Bool}({(value ? CSharpLiterals.True : CSharpLiterals.False)})";

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

    private static bool HasHookResultAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), DotBoxDMetadataNames.HookResultAttribute, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
