using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

/// <summary>
/// Merges an ordered sequence of mergeable-IR <see cref="LoweredPipelineStep"/> fragments into one complete,
/// verifiable <see cref="SandboxModule"/>. This is the runtime counterpart to the source generator's
/// build-time hook-chain fusion: a consumer that collected steps from a custom pipeline surface can combine
/// them by hand, which is exactly what "delete the attribute and hand-write it" requires.
/// </summary>
/// <remarks>
/// The composed module exposes two entrypoints over the pipeline input record:
/// <list type="bullet">
///   <item><description><c>ShouldHandle(input) -> Bool</c>: threads the input through the chain, returning
///     <c>false</c> as soon as any filter fails, otherwise <c>true</c>.</description></item>
///   <item><description><c>Handle(input) -> ResultType</c>: applies the projections in order (filters are
///     already gated by <c>ShouldHandle</c>) and returns the final projected value.</description></item>
/// </list>
/// Each step's <c>$dotboxd.current</c> placeholder is rewritten to the scoped variable holding the value that
/// flows into that step, so projections compose without name capture.
/// <para>
/// Because the two entrypoints are independent, a projection that a later filter depends on is evaluated both
/// in <c>ShouldHandle</c> (to gate) and again in <c>Handle</c> (to produce the result). The lowered fragments
/// the generator emits are pure, so this is a redundant computation rather than a duplicated side effect;
/// <c>ShouldHandle</c> stops at the last filter, so a projection with no filter after it is not evaluated
/// there at all.
/// </para>
/// </remarks>
public static class LoweredPipelineComposer
{
    private const string CurrentPlaceholder = "$dotboxd.current";
    private const string CurrentVariablePrefix = "current";
    private const string RequiredCapabilitiesMetadataKey = "dotboxd.requiredCapabilities";
    private const string EffectsMetadataKey = "dotboxd.effects";
    private static readonly SourceSpan Span = new(1, 1);

    public static SandboxModule Compose(LoweredPipelineComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrEmpty(composition.ModuleId);
        var steps = composition.Steps;
        if (steps.Count == 0)
        {
            throw new ArgumentException("A pipeline composition requires at least one step.", nameof(composition));
        }

        var inputType = ValidateAndInputType(steps);
        ValidateResultType(steps, composition.ResultType, inputType);

        var shouldHandle = BuildShouldHandle(steps, inputType, composition.ShouldHandleFunctionId);
        var handle = BuildHandle(steps, inputType, composition.ResultType, composition.HandleFunctionId);

        return new SandboxModule(
            composition.ModuleId,
            composition.Version,
            composition.TargetSandboxVersion,
            [],
            [shouldHandle, handle],
            BuildMetadata(steps));
    }

    // Each step declares its input via the single $dotboxd.current placeholder parameter. The running value
    // type only changes at a projection (its OutputType), so a filter's input must equal the value type that
    // reached it. A mismatch means the steps were not produced (or ordered) as one coherent pipeline.
    private static SandboxType ValidateAndInputType(IReadOnlyList<LoweredPipelineStep> steps)
    {
        var inputType = CurrentParameter(steps[0], 0).Type;
        var currentTag = steps[0].InputType;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            _ = CurrentParameter(step, i);
            if (step.Kind is not (LoweredPipelineStepKind.Filter or LoweredPipelineStepKind.Projection))
            {
                throw new ArgumentException(
                    $"step {i} has unsupported kind '{step.Kind}'; only Filter and Projection compose.");
            }

            if (step.Prefix.Count != 0)
            {
                throw new NotSupportedException(
                    $"step {i} carries prefix statements, which the composer does not yet support.");
            }

            if (!string.Equals(step.InputType, currentTag, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"step {i} input shape '{step.InputType}' does not match the running pipeline shape '{currentTag}'.");
            }

            if (step.Kind == LoweredPipelineStepKind.Projection)
            {
                currentTag = step.OutputType;
            }
        }

        return inputType;
    }

    private static Parameter CurrentParameter(LoweredPipelineStep step, int index)
    {
        if (step.Parameters.Count != 1 ||
            !string.Equals(step.Parameters[0].Name, CurrentPlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"step {index} must declare exactly one '{CurrentPlaceholder}' parameter.");
        }

        return step.Parameters[0];
    }

    // Handle returns the value produced by the last projection; when the pipeline has no projection at all it
    // returns the input record, so ResultType is checkable only in that case. A trailing filter after a
    // projection (e.g. Select(...).Where(...)) does not change the flowing value, so it must not force
    // ResultType back to the input type. A projected output shape is one the fragment only carries as a
    // manifest tag, so the composer trusts the caller-supplied ResultType there and leaves any final mismatch
    // to the verifier.
    private static void ValidateResultType(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType resultType,
        SandboxType inputType)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        var hasProjection = false;
        foreach (var step in steps)
        {
            if (step.Kind == LoweredPipelineStepKind.Projection)
            {
                hasProjection = true;
                break;
            }
        }

        if (!hasProjection && resultType != inputType)
        {
            throw new ArgumentException(
                "ResultType must equal the pipeline input type when the pipeline has no projection.");
        }
    }

    // Gating only needs the steps up to and including the LAST filter: a projection after the final filter can
    // never change whether the event is handled, so recomputing it here is pure waste (and would run an
    // effectful projection twice, once here and once in Handle). Projections BEFORE the last filter are still
    // emitted, because a later filter reads the value they produce.
    private static SandboxFunction BuildShouldHandle(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType inputType,
        string functionId)
    {
        var body = new List<Statement>();
        var current = InitialVariable();
        var lastFilter = LastFilterIndex(steps);
        for (var i = 0; i <= lastFilter; i++)
        {
            var step = steps[i];
            var value = Rewrite(step.Value, current);
            if (step.Kind == LoweredPipelineStepKind.Filter)
            {
                body.Add(new IfStatement(
                    new UnaryExpression("!", value, Span),
                    [new ReturnStatement(Bool(false), Span)],
                    [],
                    Span));
            }
            else
            {
                current = NextVariable(current);
                body.Add(new AssignmentStatement(current, value, Span));
            }
        }

        body.Add(new ReturnStatement(Bool(true), Span));
        return new SandboxFunction(functionId, IsEntrypoint: true, [InputParameter(inputType)], SandboxType.Bool, body);
    }

    private static int LastFilterIndex(IReadOnlyList<LoweredPipelineStep> steps)
    {
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            if (steps[i].Kind == LoweredPipelineStepKind.Filter)
            {
                return i;
            }
        }

        return -1;
    }

    private static SandboxFunction BuildHandle(
        IReadOnlyList<LoweredPipelineStep> steps,
        SandboxType inputType,
        SandboxType resultType,
        string functionId)
    {
        var body = new List<Statement>();
        var current = InitialVariable();
        foreach (var step in steps)
        {
            if (step.Kind != LoweredPipelineStepKind.Projection)
            {
                continue;
            }

            var value = Rewrite(step.Value, current);
            current = NextVariable(current);
            body.Add(new AssignmentStatement(current, value, Span));
        }

        body.Add(new ReturnStatement(new VariableExpression(current, Span), Span));
        return new SandboxFunction(functionId, IsEntrypoint: true, [InputParameter(inputType)], resultType, body);
    }

    private static Parameter InputParameter(SandboxType type) => new(InitialVariable(), type);

    private static string InitialVariable() => CurrentVariablePrefix + "0";

    private static string NextVariable(string current)
        => CurrentVariablePrefix + (int.Parse(current[CurrentVariablePrefix.Length..], System.Globalization.CultureInfo.InvariantCulture) + 1);

    private static LiteralExpression Bool(bool value) => new(SandboxValue.FromBool(value), Span);

    // Rewrites the fragment's $dotboxd.current placeholder to the scoped variable that currently holds the
    // flowing value. The lowered fragment expressions are the closed set the generator emits.
    private static Expression Rewrite(Expression expression, string current)
        => expression switch
        {
            VariableExpression { Name: CurrentPlaceholder } variable => new VariableExpression(current, variable.Span),
            VariableExpression variable => throw new NotSupportedException(
                $"a fragment expression may only reference the '{CurrentPlaceholder}' placeholder, not the " +
                $"variable '{variable.Name}' (which could collide with the composer's reserved running-value slots)."),
            LiteralExpression => expression,
            UnaryExpression unary => new UnaryExpression(unary.Operator, Rewrite(unary.Operand, current), unary.Span),
            BinaryExpression binary => new BinaryExpression(
                Rewrite(binary.Left, current), binary.Operator, Rewrite(binary.Right, current), binary.Span),
            CallExpression call => new CallExpression(
                call.Name, RewriteAll(call.Arguments, current), call.GenericType, call.Span),
            _ => throw new NotSupportedException(
                $"the composer cannot rewrite a '{expression.GetType().Name}' fragment expression.")
        };

    private static IReadOnlyList<Expression> RewriteAll(IReadOnlyList<Expression> expressions, string current)
    {
        var rewritten = new Expression[expressions.Count];
        for (var i = 0; i < expressions.Count; i++)
        {
            rewritten[i] = Rewrite(expressions[i], current);
        }

        return rewritten;
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(IReadOnlyList<LoweredPipelineStep> steps)
    {
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            capabilities.UnionWith(step.RequiredCapabilities);
            effects.UnionWith(step.Effects);
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (capabilities.Count != 0)
        {
            metadata[RequiredCapabilitiesMetadataKey] = string.Join(",", capabilities);
        }

        if (effects.Count != 0)
        {
            metadata[EffectsMetadataKey] = string.Join(",", effects);
        }

        return metadata;
    }
}
