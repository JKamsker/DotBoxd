using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Lowers an anonymous-object projection — e.g. <c>Select((e, ctx) =&gt; new { Id = e.MonsterId, N = ... })</c> —
/// into the sandbox <c>record.new</c> intrinsic, identical to a named DTO. The synthesized anonymous type is a
/// structural record: its public properties (declaration order) become the positional record fields, so a
/// downstream <c>Where</c>/<c>Select</c> can read <c>x.Id</c>/<c>x.N</c> via <c>record.get</c>.
///
/// An anonymous type is usable only as an INTERMEDIATE, server-side projection: it cannot be the value pushed to
/// <c>RunLocal</c>, because the generated interceptor's handler parameter must name the projected type and an
/// anonymous type has no nameable identity. Such a terminal projection is rejected upstream; here we only build
/// the server-side IR.
/// </summary>
internal static class DotBoxDAnonymousObjectCreationExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        AnonymousObjectCreationExpressionSyntax creation,
        DotBoxDExpressionLoweringContext context,
        System.Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type
                is not INamedTypeSymbol { IsAnonymousType: true } anonType)
        {
            return null;
        }

        if (SandboxTypeSourceEmitter.TryEmit(anonType) is not { } recordTypeSource)
        {
            throw new System.NotSupportedException();
        }

        var fields = DotBoxDRpcTypeMapper.RecordFields(anonType);
        var initializers = creation.Initializers;
        if (initializers.Count != fields.Count)
        {
            throw new System.NotSupportedException();
        }

        // record.new wants its arguments in the anonymous type's declared property order. Map each declared field
        // to the initializer that fills it (by member name) and lower it with the field's expected type.
        var fieldSources = new string[fields.Count];
        var allocates = true;
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var initializerIndex = InitializerIndex(initializers, fields[fieldIndex].Name);
            if (initializerIndex < 0)
            {
                throw new System.NotSupportedException();
            }

            var lowered = lowerExpression(initializers[initializerIndex].Expression);
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

    // The initializer that fills the field named <paramref name="fieldName"/>. An initializer either names its
    // member explicitly (<c>Name = expr</c>) or infers it from a projection (<c>e.MonsterId</c> → <c>MonsterId</c>).
    private static int InitializerIndex(
        SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax> initializers,
        string fieldName)
    {
        for (var i = 0; i < initializers.Count; i++)
        {
            if (string.Equals(InitializerName(initializers[i]), fieldName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? InitializerName(AnonymousObjectMemberDeclaratorSyntax declarator)
    {
        if (declarator.NameEquals is { } nameEquals)
        {
            return nameEquals.Name.Identifier.ValueText;
        }

        return declarator.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
    }
}
