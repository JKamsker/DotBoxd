using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageLowerer
{
    public static DotBoxDStatementBodyModel CreateShouldHandle(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => BuildShouldHandle(
            stages,
            index: 0,
            current: null,
            currentType: null,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);

    public static DotBoxDHandleModel CreateHandle(
        IReadOnlyList<HookChainStage> stages,
        string terminalElementParam,
        string? terminalContextParam,
        ITypeSymbol? terminalContextType,
        InvocationExpressionSyntax sendInvocation,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var projection = ApplySelects(stages, eventProperties, model, cancellationToken, capabilities, effects);

        var context = Context(
            terminalElementParam,
            terminalContextParam,
            terminalContextType,
            eventProperties,
            projection.Current,
            projection.CurrentType,
            model,
            cancellationToken,
            capabilities,
            effects);
        var handle = DotBoxDHandleModelFactory.CreateFromSend(sendInvocation, context);
        return new DotBoxDHandleModel(handle.Target, handle.Message, projection.Prefix);
    }

    public static HookChainProjection? CreateProjection(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var projection = ApplySelects(stages, eventProperties, model, cancellationToken, capabilities, effects);
        return projection.Current is null
            ? null
            : new HookChainProjection(projection.Prefix, projection.Current, projection.CurrentType);
    }

    private static ProjectionState ApplySelects(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        DotBoxDExpressionModel? current = null;
        ITypeSymbol? currentType = null;
        DotBoxDStatementBodyModel? prefix = null;
        for (var i = 0; i < stages.Count; i++)
        {
            if (!stages[i].IsSelect)
            {
                continue;
            }

            var projection = LowerSelect(stages[i], current, currentType, eventProperties, model, cancellationToken, capabilities, effects);
            prefix = prefix is null
                ? projection.Assignment
                : DotBoxDStatementBodyModelFactory.Concat(prefix, projection.Assignment);
            current = projection.Current;
            currentType = projection.CurrentType;
        }

        return new ProjectionState(prefix, current, currentType);
    }

    private static DotBoxDStatementBodyModel BuildShouldHandle(
        IReadOnlyList<HookChainStage> stages,
        int index,
        DotBoxDExpressionModel? current,
        ITypeSymbol? currentType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        if (index >= stages.Count)
        {
            return DotBoxDConditionBodyModelFactory.AlwaysTrue();
        }

        var stage = stages[index];
        if (stage.IsSelect)
        {
            if (!HasWhereAtOrAfter(stages, index + 1))
            {
                return DotBoxDConditionBodyModelFactory.AlwaysTrue();
            }

            var projection = LowerSelect(stage, current, currentType, eventProperties, model, cancellationToken, capabilities, effects);
            var next = BuildShouldHandle(
                stages,
                index + 1,
                projection.Current,
                projection.CurrentType,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            return DotBoxDStatementBodyModelFactory.Concat(projection.Assignment, next);
        }

        var (elementParam, contextParam) = LambdaParameters(stage.Lambda);
        if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var context = Context(
            elementParam,
            contextParam,
            LambdaParameterType(stage.Lambda, contextParam, model, cancellationToken),
            eventProperties,
            current,
            currentType,
            model,
            cancellationToken,
            capabilities,
            effects);
        var whenTrue = BuildShouldHandle(
            stages,
            index + 1,
            current,
            currentType,
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);
        return DotBoxDConditionBodyModelFactory.CreateBranch(
            body,
            whenTrue,
            DotBoxDConditionBodyModelFactory.AlwaysFalse(),
            context);
    }

    private static Projection LowerSelect(
        HookChainStage stage,
        DotBoxDExpressionModel? current,
        ITypeSymbol? currentType,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var (elementParam, contextParam) = LambdaParameters(stage.Lambda);
        if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var context = Context(
            elementParam,
            contextParam,
            LambdaParameterType(stage.Lambda, contextParam, model, cancellationToken),
            eventProperties,
            current,
            currentType,
            model,
            cancellationToken,
            capabilities,
            effects);
        var value = DotBoxDExpressionModelFactory.Create(body, context);
        var name = SelectTemp(stage.Lambda);

        // Carry the projection's CLR type so a downstream stage can read its fields by name (record.get).
        var bodyTypeInfo = model.GetTypeInfo(body, cancellationToken);
        var bodyType = bodyTypeInfo.ConvertedType ?? bodyTypeInfo.Type;

        return new Projection(
            DotBoxDStatementBodyModelFactory.Assign(name, value),
            DotBoxDStatementBodyModelFactory.Variable(name, value.Type),
            bodyType);
    }

    private static DotBoxDExpressionLoweringContext Context(
        string elementParam,
        string? contextParam,
        ITypeSymbol? contextType,
        EquatableArray<EventPropertyModel> eventProperties,
        DotBoxDExpressionModel? current,
        ITypeSymbol? currentType,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => new(
            elementParam,
            eventProperties,
            default,
            model,
            cancellationToken,
            projectedElementName: current is null ? null : elementParam,
            projectedElement: current,
            projectedElementType: current is null ? null : currentType,
            serverContextParameterName: contextParam,
            serverContextType: contextType,
            capabilities: capabilities,
            effects: effects);

    private static bool HasWhereAtOrAfter(IReadOnlyList<HookChainStage> stages, int index)
    {
        for (var i = index; i < stages.Count; i++)
        {
            if (!stages[i].IsSelect)
            {
                return true;
            }
        }

        return false;
    }

    private static string SelectTemp(LambdaExpressionSyntax lambda)
        => "$dotboxd.select." + lambda.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static (string? ElementParam, string? ContextParam) LambdaParameters(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, null),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters } => parameters.Count switch
            {
                1 => (parameters[0].Identifier.ValueText, null),
                2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText),
                _ => (null, null),
            },
            _ => (null, null)
        };

    private static ITypeSymbol? LambdaParameterType(
        LambdaExpressionSyntax lambda,
        string? parameterName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (parameterName is null)
        {
            return null;
        }

        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: var parameters })
        {
            foreach (var parameter in parameters)
            {
                if (string.Equals(parameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                {
                    var type = (model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)?.Type;
                    return type is { TypeKind: not TypeKind.Error }
                        ? type
                        : GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
                }
            }
        }

        return GeneratedRemoteHookChainFallback.ServerContextTypeForLambda(lambda, model, cancellationToken);
    }

    private sealed record Projection(DotBoxDStatementBodyModel Assignment, DotBoxDExpressionModel Current, ITypeSymbol? CurrentType);
    private sealed record ProjectionState(DotBoxDStatementBodyModel? Prefix, DotBoxDExpressionModel? Current, ITypeSymbol? CurrentType);
}

internal sealed record HookChainProjection(DotBoxDStatementBodyModel? Prefix, DotBoxDExpressionModel Value, ITypeSymbol? ValueType);
