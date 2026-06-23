using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    // Generates the reflection-free reader for the pushed value's projected type, mirroring the type the
    // terminal handler receives: the final Select element type, or the whole event when there is no Select.
    // Returns null when the type is not wire-eligible (the reader emitter declines) so the chain falls back to
    // the reflective registration.
    private static string? BuildLocalDecoderSource(ITypeSymbol? projectedTypeSymbol)
        => projectedTypeSymbol is null
            ? null
            : Rpc.RpcLocalDecoderEmitter.TryEmit(projectedTypeSymbol);

    // The CLR type the pushed value decodes to: the final Select body's type (using the same
    // ConvertedType ?? Type resolution as TerminalElementTypeFullName so the generated ReadProjected return
    // type matches the handler's projected type), or the event type when the chain has no Select.
    private static ITypeSymbol? ProjectedTypeSymbol(
        IReadOnlyList<HookChainStage> stages,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        ITypeSymbol projected = eventType;
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                continue;
            }

            if (stage.Lambda.ExpressionBody is not { } body)
            {
                return null;
            }

            var typeInfo = model.GetTypeInfo(body, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type is null || type.TypeKind == TypeKind.Error)
            {
                return null;
            }

            projected = type;
        }

        return projected;
    }

    private static EquatableArray<string> ManifestEffects(
        HookChainInterceptorInstallKind installKind,
        DotBoxDStatementBodyModel shouldHandle,
        DotBoxDStatementBodyModel handleBody,
        ICollection<string> effects)
        => installKind == HookChainInterceptorInstallKind.LocalCallback
            ? DotBoxDManifestEffectModel.CreateLocalCallback(shouldHandle, handleBody, effects)
            : DotBoxDManifestEffectModel.Create(shouldHandle, handleBody, effects);

    private static DotBoxDStatementBodyModel LocalCallbackHandleBody(HookChainProjection? projection)
        => projection is null
            ? DotBoxDHandleBodyModelFactory.ReturnUnit()
            : DotBoxDHandleBodyModelFactory.ReturnExpression(projection.Value, projection.Prefix);

    private static string LocalCallbackHandleReturnType(
        HookChainProjection? projection,
        ITypeSymbol? projectedTypeSymbol)
    {
        if (projection is null)
        {
            // Whole-event push: the Handle returns Unit and the host pushes the event record itself.
            return DotBoxDGenerationNames.TypeNames.GlobalSandboxType + ".Unit";
        }

        // A projection chain's Handle returns the projected value, so its return type is the full SandboxType the
        // projected CLR type maps to (scalar, Guid, enum, list, map, or DTO record). A projection whose type is
        // not marshaller-eligible yields no source and the chain fails safe (TryCreate returns null, no package).
        if (projectedTypeSymbol is not null && Rpc.SandboxTypeSourceEmitter.TryEmit(projectedTypeSymbol) is { } source)
        {
            return source;
        }

        throw new NotSupportedException();
    }

    private static DotBoxDStatementBodyModel LowerSendHandle(
        IReadOnlyList<HookChainStage> stages,
        LambdaExpressionSyntax terminalLambda,
        string terminalElementParam,
        string? terminalContextParam,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (terminalContextParam is null ||
            terminalLambda.ExpressionBody is not InvocationExpressionSyntax sendInvocation ||
            !DotBoxDHandleModelFactory.IsContextSend(sendInvocation.Expression, terminalContextParam))
        {
            throw new NotSupportedException();
        }

        var handle = HookChainStageLowerer.CreateHandle(
            stages,
            terminalElementParam,
            sendInvocation,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        return DotBoxDHandleBodyModelFactory.FromSend(handle);
    }

    private static string TerminalElementTypeFullName(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        INamedTypeSymbol eventType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        DotBoxDExpressionModel? projected = null;
        ITypeSymbol? projectedType = null;
        var terminalElementTypeFullName = eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var stage in stages)
        {
            if (!stage.IsSelect)
            {
                continue;
            }

            var (elementParam, _, _) = LambdaParameters(stage.Lambda);
            if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
            {
                throw new NotSupportedException();
            }

            var scratchCapabilities = new SortedSet<string>(StringComparer.Ordinal);
            var scratchEffects = new SortedSet<string>(StringComparer.Ordinal);
            projected = DotBoxDExpressionModelFactory.Create(
                body,
                Context(elementParam, eventProperties, projected, projectedType, model, cancellationToken, scratchCapabilities, scratchEffects));
            var bodyTypeInfo = model.GetTypeInfo(body, cancellationToken);
            projectedType = bodyTypeInfo.ConvertedType ?? bodyTypeInfo.Type;
            terminalElementTypeFullName = GeneratedRemoteHookChainFallback.TypeFullName(
                body,
                model,
                cancellationToken,
                projected.Type);
        }

        return terminalElementTypeFullName;
    }

    private static DotBoxDExpressionLoweringContext Context(
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        DotBoxDExpressionModel? projected,
        ITypeSymbol? projectedType,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => projected is null
            ? new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                capabilities: capabilities, effects: effects)
            : new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                projectedElementName: elementParam,
                projectedElement: projected,
                projectedElementType: projectedType,
                capabilities: capabilities,
                effects: effects);
}
