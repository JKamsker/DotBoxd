using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class GeneratorGuard
{
    public static IncrementalValuesProvider<T> AttributeValues<T>(
        IncrementalGeneratorInitializationContext context,
        string metadataName,
        Func<SyntaxNode, CancellationToken, bool> predicate,
        string stage,
        Func<GeneratorAttributeSyntaxContext, CancellationToken, T?> create)
        where T : class
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            metadataName,
            predicate,
            (ctx, ct) => TryCreate(stage, ctx, ct, create));
        RegisterDiagnostics(context, results);
        return Values(results);
    }

    public static IncrementalValuesProvider<T> SyntaxValues<T>(
        IncrementalGeneratorInitializationContext context,
        Func<SyntaxNode, CancellationToken, bool> predicate,
        string stage,
        Func<GeneratorSyntaxContext, CancellationToken, T?> create)
        where T : class
    {
        var results = context.SyntaxProvider.CreateSyntaxProvider(
            predicate,
            (ctx, ct) => TryCreate(stage, ctx, ct, create));
        RegisterDiagnostics(context, results);
        return Values(results);
    }

    public static IncrementalValuesProvider<T> TransformValues<TInput, T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TInput> source,
        string stage,
        Func<TInput, CancellationToken, T> transform)
    {
        var results = source.Select((input, ct) => TryTransform(stage, input, ct, transform));
        RegisterDiagnostics(context, results);
        return Values(results);
    }

    public static IncrementalValuesProvider<T> TransformNullableValues<TInput, T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TInput> source,
        string stage,
        Func<TInput, CancellationToken, T?> transform)
        where T : class
    {
        var results = source.Select((input, ct) => TryCreate(stage, input, ct, transform));
        RegisterDiagnostics(context, results);
        return Values(results);
    }

    public static IncrementalValueProvider<T> TransformValueOrDefault<TInput, T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<TInput> source,
        string stage,
        Func<TInput, CancellationToken, T> transform)
    {
        var result = source.Select((input, ct) => TryTransform(stage, input, ct, transform));
        RegisterDiagnostics(context, result);
        return ValueOrDefault(result);
    }

    public static void RegisterOutput<T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<T> source,
        string stage,
        Action<SourceProductionContext, T> output)
        => context.RegisterSourceOutput(
            source,
            (sourceContext, value) => TryEmit(sourceContext, stage, value, output));

    public static void RegisterOutput<T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<T> source,
        string stage,
        Action<SourceProductionContext, T> output)
        => context.RegisterSourceOutput(
            source,
            (sourceContext, value) => TryEmit(sourceContext, stage, value, output));

    public static GeneratorGuardedValue<T> TryCreate<T>(
        string stage,
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken,
        Func<GeneratorAttributeSyntaxContext, CancellationToken, T?> create)
        where T : class
        => TryCreate(
            stage,
            context,
            cancellationToken,
            create,
            static context => context.TargetNode.GetLocation());

    public static GeneratorGuardedValue<T> TryCreate<T>(
        string stage,
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken,
        Func<GeneratorSyntaxContext, CancellationToken, T?> create)
        where T : class
        => TryCreate(
            stage,
            context,
            cancellationToken,
            create,
            static context => context.Node.GetLocation());

    public static GeneratorGuardedValue<T> TryCreate<TInput, T>(
        string stage,
        TInput input,
        CancellationToken cancellationToken,
        Func<TInput, CancellationToken, T?> create,
        Func<TInput, Location?>? location = null)
        where T : class
    {
        try
        {
            var value = create(input, cancellationToken);
            return value is null ? default : GeneratorGuardedValue<T>.Success(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GeneratorGuardedValue<T>.Failure(Failure(stage, ex, location?.Invoke(input)));
        }
    }

    public static GeneratorGuardedValue<T> TryTransform<TInput, T>(
        string stage,
        TInput input,
        CancellationToken cancellationToken,
        Func<TInput, CancellationToken, T> transform,
        Func<TInput, Location?>? location = null)
    {
        try
        {
            return GeneratorGuardedValue<T>.Success(transform(input, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GeneratorGuardedValue<T>.Failure(Failure(stage, ex, location?.Invoke(input)));
        }
    }

    public static IncrementalValuesProvider<T> Values<T>(
        IncrementalValuesProvider<GeneratorGuardedValue<T>> results)
        => results
            .Where(static result => result.HasValue)
            .Select(static (result, _) => result.Value!);

    public static IncrementalValueProvider<T> ValueOrDefault<T>(
        IncrementalValueProvider<GeneratorGuardedValue<T>> result)
        => result.Select(static (result, _) => result.HasValue ? result.Value! : default!);

    public static void RegisterDiagnostics<T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<GeneratorGuardedValue<T>> results)
        => context.RegisterSourceOutput(
            results
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

    public static void RegisterDiagnostics<T>(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<GeneratorGuardedValue<T>> result)
        => context.RegisterSourceOutput(
            result,
            static (sourceContext, result) =>
            {
                if (result.Diagnostic is not null)
                {
                    sourceContext.ReportDiagnostic(result.Diagnostic.ToDiagnostic());
                }
            });

    public static void TryEmit<T>(
        SourceProductionContext context,
        string stage,
        T value,
        Action<SourceProductionContext, T> emit)
    {
        try
        {
            emit(context, value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Failure(stage, ex, null).ToDiagnostic());
        }
    }

    private static GeneratorFailureDiagnostic Failure(string stage, Exception exception, Location? location)
        => new(
            stage,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            location is { IsInSource: true } ? PluginDiagnosticLocation.From(location) : null);
}
