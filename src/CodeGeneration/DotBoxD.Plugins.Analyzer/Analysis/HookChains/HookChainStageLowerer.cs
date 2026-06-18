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
            eventProperties,
            model,
            cancellationToken,
            capabilities,
            effects);

    public static DotBoxDHandleModel CreateHandle(
        IReadOnlyList<HookChainStage> stages,
        string terminalElementParam,
        InvocationExpressionSyntax sendInvocation,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        DotBoxDExpressionModel? current = null;
        DotBoxDStatementBodyModel? prefix = null;
        for (var i = 0; i < stages.Count; i++)
        {
            if (!stages[i].IsSelect)
            {
                continue;
            }

            var projection = LowerSelect(
                stages[i],
                current,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            prefix = prefix is null
                ? projection.Assignment
                : DotBoxDStatementBodyModelFactory.Concat(prefix, projection.Assignment);
            current = projection.Current;
        }

        var context = Context(
            terminalElementParam,
            eventProperties,
            current,
            model,
            cancellationToken,
            capabilities,
            effects);
        var handle = DotBoxDHandleModelFactory.CreateFromSend(sendInvocation, context);
        return new DotBoxDHandleModel(handle.Target, handle.Message, prefix);
    }

    public static HookChainProjection? CreateProjection(
        IReadOnlyList<HookChainStage> stages,
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        DotBoxDExpressionModel? current = null;
        DotBoxDStatementBodyModel? prefix = null;
        for (var i = 0; i < stages.Count; i++)
        {
            if (!stages[i].IsSelect)
            {
                continue;
            }

            var projection = LowerSelect(
                stages[i],
                current,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            prefix = prefix is null
                ? projection.Assignment
                : DotBoxDStatementBodyModelFactory.Concat(prefix, projection.Assignment);
            current = projection.Current;
        }

        return current is null
            ? null
            : new HookChainProjection(prefix, current);
    }

    private static DotBoxDStatementBodyModel BuildShouldHandle(
        IReadOnlyList<HookChainStage> stages,
        int index,
        DotBoxDExpressionModel? current,
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

            var projection = LowerSelect(stage, current, eventProperties, model, cancellationToken, capabilities, effects);
            var next = BuildShouldHandle(
                stages,
                index + 1,
                projection.Current,
                eventProperties,
                model,
                cancellationToken,
                capabilities,
                effects);
            return DotBoxDStatementBodyModelFactory.Concat(projection.Assignment, next);
        }

        var (elementParam, _) = LambdaParameters(stage.Lambda);
        if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var context = Context(elementParam, eventProperties, current, model, cancellationToken, capabilities, effects);
        var whenTrue = BuildShouldHandle(
            stages,
            index + 1,
            current,
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
        EquatableArray<EventPropertyModel> eventProperties,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
    {
        var (elementParam, _) = LambdaParameters(stage.Lambda);
        if (elementParam is null || stage.Lambda.ExpressionBody is not { } body)
        {
            throw new NotSupportedException();
        }

        var context = Context(elementParam, eventProperties, current, model, cancellationToken, capabilities, effects);
        var value = DotBoxDExpressionModelFactory.Create(body, context);
        var name = SelectTemp(stage.Lambda);
        return new Projection(
            DotBoxDStatementBodyModelFactory.Assign(name, value),
            DotBoxDStatementBodyModelFactory.Variable(name, value.Type));
    }

    private static DotBoxDExpressionLoweringContext Context(
        string elementParam,
        EquatableArray<EventPropertyModel> eventProperties,
        DotBoxDExpressionModel? current,
        SemanticModel model,
        CancellationToken cancellationToken,
        ICollection<string> capabilities,
        ICollection<string> effects)
        => current is null
            ? new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken,
                capabilities: capabilities, effects: effects)
            : new DotBoxDExpressionLoweringContext(
                elementParam, eventProperties, default, model, cancellationToken, elementParam, current,
                capabilities, effects);

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
    {
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                return (simple.Parameter.Identifier.ValueText, null);
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                var parameters = parenthesized.ParameterList.Parameters;
                return parameters.Count switch
                {
                    1 => (parameters[0].Identifier.ValueText, null),
                    2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText),
                    _ => (null, null),
                };
            default:
                return (null, null);
        }
    }

    private sealed record Projection(
        DotBoxDStatementBodyModel Assignment,
        DotBoxDExpressionModel Current);
}

internal sealed record HookChainProjection(
    DotBoxDStatementBodyModel? Prefix,
    DotBoxDExpressionModel Value);
