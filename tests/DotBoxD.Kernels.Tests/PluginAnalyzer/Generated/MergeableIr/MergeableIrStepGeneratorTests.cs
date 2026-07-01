using System.Collections.Immutable;
using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class MergeableIrStepGeneratorTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Generator_lowers_marked_filter_and_projection_to_mergeable_steps()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using System.Collections.Generic;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance, string TargetId);

            public sealed class StepPipeline<T>
            {
                private readonly List<LoweredPipelineStep> _steps;

                public StepPipeline() : this(new List<LoweredPipelineStep>()) { }

                private StepPipeline(List<LoweredPipelineStep> steps) => _steps = steps;

                public IReadOnlyList<LoweredPipelineStep> Steps => _steps;

                public StepPipeline<T> Where([LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => throw new InvalidOperationException("not lowered");

                public StepPipeline<T> Where(LoweredPipelineStep step)
                {
                    _steps.Add(step);
                    return this;
                }

                public StepPipeline<TNext> Select<TNext>(
                    [LowerToIr(LoweredPipelineStepKind.Projection)] Func<T, TNext> selector)
                    => throw new InvalidOperationException("not lowered");

                public StepPipeline<TNext> Select<TNext>(LoweredPipelineStep step)
                {
                    _steps.Add(step);
                    return new StepPipeline<TNext>(_steps);
                }
            }

            public static class Usage
            {
                public static StepPipeline<string> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(e => e.Distance >= 4).Select(e => e.TargetId);
            }
            """);

        var generated = GeneratedSource(result);

        Assert.Contains("LoweredPipelineStepKind.Filter", generated);
        Assert.Contains("LoweredPipelineStepKind.Projection", generated);
        Assert.Contains("Var(\"$dotboxd.current\")", generated);
        Assert.Contains("\"record.get\"", generated);
        Assert.Contains("\"probe.read.distance\"", generated);
        Assert.Contains("DotBoxDMergeableIrStepInterceptors", GeneratedHintNames(result));
    }

    [Fact]
    public void Generator_reports_marked_receiver_without_lowered_step_overload()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class BadPipeline<T>
            {
                public BadPipeline<T> Where([LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => this;
            }

            public static class Usage
            {
                public static BadPipeline<int> Configure(BadPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_reports_diagnostic_for_malformed_marked_call_without_crashing()
    {
        // A [LowerToIr(Filter)] parameter whose delegate does not return bool makes the marked-call
        // reader throw NotSupportedException. That reader runs before the model factory's try/catch,
        // so before the fix it escaped as an unhandled generator exception. It must instead surface as
        // a DBXK100 diagnostic with no emitted step.
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class FilterPipeline<T>
            {
                public FilterPipeline<T> Where([LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, int> predicate)
                    => throw new InvalidOperationException("not lowered");

                public FilterPipeline<T> Where(LoweredPipelineStep step)
                    => this;
            }

            public static class Usage
            {
                public static FilterPipeline<int> Configure(FilterPipeline<int> pipeline)
                    => pipeline.Where(value => value);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Id == "DBXK100");
        Assert.Contains("filter steps must return bool", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_reports_diagnostic_for_an_anonymous_projection()
    {
        // Select(e => new { ... }) projects to an anonymous type with no C#-nameable form, so emitting the
        // interceptor would produce uncompilable source. It must surface as a DBXK100 diagnostic with no step.
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class ProjectionPipeline<T>
            {
                public ProjectionPipeline<object> Select<TOut>([LowerToIr(LoweredPipelineStepKind.Projection)] Func<T, TOut> projection)
                    => throw new InvalidOperationException("not lowered");

                public ProjectionPipeline<object> Select(LoweredPipelineStep step)
                    => this;
            }

            public static class Usage
            {
                public static ProjectionPipeline<object> Configure(ProjectionPipeline<int> pipeline)
                    => pipeline.Select(value => new { Doubled = value * 2 });
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Id == "DBXK100");
        Assert.Contains(
            "anonymous-type projections",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult RunGeneratorAndAssertCompiles(string source)
    {
        var result = RunGenerator(source, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Diagnostics);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return result;
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
        => RunGenerator(source, out _, out _);

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        out Compilation outputCompilation,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDMergeableIrStepGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out diagnostics);
        return driver.GetRunResult();
    }

    private static string GeneratedSource(GeneratorDriverRunResult result)
        => string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

    private static string GeneratedHintNames(GeneratorDriverRunResult result)
        => string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => Path.GetFileName(tree.FilePath)));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
