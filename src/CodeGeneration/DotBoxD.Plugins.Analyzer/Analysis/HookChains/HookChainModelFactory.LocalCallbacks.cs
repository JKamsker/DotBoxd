using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainProjection? LocalCallbackProjection(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var projection = HookChainStageLowerer.CreateProjection(
            stages,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        return projection ?? WholeEventProjection(eventType, eventProperties, capabilities, effects);
    }

    private static HookChainProjection WholeEventProjection(
        INamedTypeSymbol eventType,
        EquatableArray<EventPropertyModel> eventProperties,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var sandboxType = Rpc.SandboxTypeSourceEmitter.TryEmit(eventType) ?? throw new NotSupportedException();
        var fields = new string[eventProperties.Count];
        for (var i = 0; i < eventProperties.Count; i++)
        {
            if (eventProperties[i].Capability is { Length: > 0 } capability)
            {
                capabilities.Add(capability);
            }

            fields[i] = DotBoxDGenerationNames.Helpers.Var + "(" +
                LiteralReader.StringLiteral(DotBoxDExpressionModelFactory.EventVariable(eventProperties[i].Name)) + ")";
        }

        effects.Add(DotBoxDGenerationNames.Effects.Alloc);
        return new HookChainProjection(
            Prefix: null,
            new DotBoxDExpressionModel(
                RpcRecordNew(fields, sandboxType),
                Rpc.SandboxTypeSourceEmitter.ManifestTag(eventType),
                Allocates: true),
            eventType);
    }

    private static string RpcRecordNew(IReadOnlyList<string> fields, string sandboxType)
        => "new " + DotBoxDGenerationNames.TypeNames.GlobalCallExpression + "(\"record.new\", " +
           "[" + string.Join(", ", fields) + "], " + sandboxType + ", Span)";

    private static DotBoxDStatementBodyModel LocalCallbackHandleBody(HookChainProjection? projection)
        => projection is null
            ? throw new NotSupportedException()
            : DotBoxDHandleBodyModelFactory.ReturnExpression(projection.Value, projection.Prefix);

    private static string LocalCallbackHandleReturnType(
        HookChainProjection? projection,
        ITypeSymbol? projectedTypeSymbol)
    {
        // A local-terminal chain's Handle returns the projected value, so its return type is the full
        // SandboxType the projected CLR type maps to (scalar, Guid, enum, list, map, or DTO record). No-Select
        // RunLocal chains are emitted as an explicit event-record projection, not a Unit-returning marker.
        if (projectedTypeSymbol is not null && Rpc.SandboxTypeSourceEmitter.TryEmit(projectedTypeSymbol) is { } source)
        {
            return source;
        }

        if (projection is { } value && ScalarSandboxTypeSource(value.Value.Type) is { } scalarSource)
        {
            return scalarSource;
        }

        throw new NotSupportedException();
    }

    private static string? ScalarSandboxTypeSource(string manifestTag)
        => manifestTag switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool => DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Bool",
            DotBoxDGenerationNames.ManifestTypes.Int => DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".I32",
            DotBoxDGenerationNames.ManifestTypes.Long => DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".I64",
            DotBoxDGenerationNames.ManifestTypes.Double => DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".F64",
            DotBoxDGenerationNames.ManifestTypes.String => DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".String",
            _ => null
        };
}
