using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotBoxd.Plugins.Analyzer;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAnalyzerIncrementalityTests
{
    private const string DiagnosticResult = "DotBoxdPluginDiagnosticResult";
    private const string ModelResult = "DotBoxdPluginModelResult";
    private const string PackageResult = "DotBoxdPluginPackageResult";
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generator_model_and_package_outputs_are_cacheable_and_do_not_capture_roslyn_objects()
    {
        var (first, second) = RunGeneratorTwice("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Amount);

            [Plugin("cacheable")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public int MinDamage { get; set; } = 100;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => e.Amount >= MinDamage;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        AssertTrackedStep(first, second, ModelResult);
        AssertTrackedStep(first, second, PackageResult);
    }

    [Fact]
    public void Generator_diagnostic_outputs_are_cacheable_and_do_not_capture_roslyn_objects()
    {
        var (first, second) = RunGeneratorTwice("""
            using DotBoxd.Plugins;
            using DotBoxd.Abstractions;

            namespace Sample;

            [Plugin("invalid-cacheable")]
            public sealed partial class MissingKernel
            {
            }
            """);

        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Empty(first.GeneratedTrees);
        AssertTrackedStep(first, second, DiagnosticResult);
    }

    private static (GeneratorDriverRunResult First, GeneratorDriverRunResult Second) RunGeneratorTwice(string source)
    {
        var compilation = CreateCompilation(source);
        var options = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DotBoxdPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: options);

        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult();
        var second = driver.RunGenerators(compilation.Clone()).GetRunResult();
        return (first, second);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxdPluginIncrementalityTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static void AssertTrackedStep(
        GeneratorDriverRunResult first,
        GeneratorDriverRunResult second,
        string trackingName)
    {
        var firstSteps = TrackedSteps(first, trackingName);
        var secondSteps = TrackedSteps(second, trackingName);
        Assert.Equal(firstSteps.Length, secondSteps.Length);
        for (var i = 0; i < firstSteps.Length; i++)
        {
            Assert.Equal(firstSteps[i].Outputs.Length, secondSteps[i].Outputs.Length);
            AssertTrackedOutputs(firstSteps[i], secondSteps[i], trackingName);
        }
    }

    private static void AssertTrackedOutputs(
        IncrementalGeneratorRunStep first,
        IncrementalGeneratorRunStep second,
        string trackingName)
    {
        for (var i = 0; i < first.Outputs.Length; i++)
        {
            Assert.Equal(first.Outputs[i].Value, second.Outputs[i].Value);
            AssertNoRoslynObjects(first.Outputs[i].Value);
            Assert.True(
                second.Outputs[i].Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"{trackingName} output was {second.Outputs[i].Reason} instead of cached or unchanged.");
        }
    }

    private static ImmutableArray<IncrementalGeneratorRunStep> TrackedSteps(
        GeneratorDriverRunResult result,
        string trackingName)
    {
        Assert.True(result.Results[0].TrackedSteps.TryGetValue(trackingName, out var steps), trackingName);
        Assert.NotEmpty(steps);
        return steps;
    }

    private static void AssertNoRoslynObjects(object? value)
        => Visit(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static void Visit(object? value, HashSet<object> visited)
    {
        if (value is null ||
            value is string ||
            !visited.Add(value))
        {
            return;
        }

        Assert.False(
            value is Compilation or ISymbol or SyntaxNode or Location,
            $"Tracked generator output captured Roslyn object {value.GetType().FullName}.");

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal))
        {
            return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                Visit(item, visited);
            }

            return;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Visit(field.GetValue(value), visited);
        }
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
