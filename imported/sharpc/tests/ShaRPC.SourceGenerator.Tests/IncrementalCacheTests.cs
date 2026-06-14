using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Verifies that the ShaRPC incremental source generator caches downstream value-equal
/// outputs across re-runs, both for trivia-only edits to a service interface and for
/// edits to entirely unrelated files in the same compilation.
/// </summary>
public class IncrementalCacheTests
{
    private const string Service1Default = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Demo.Svc
        {
            [ShaRpcService]
            public interface IFooService
            {
                Task<int> AddAsync(int a, int b);
                Task PingAsync();
            }
        }
        """;

    private const string UnrelatedDefault = """
        namespace Demo.Other
        {
            public class Unrelated
            {
                public int X { get; set; }
            }
        }
        """;

    private const string Service2Default = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Demo.Svc2
        {
            [ShaRpcService]
            public interface IBarService
            {
                Task<string> EchoAsync(string s);
            }
        }
        """;

    [Fact]
    public void TriviaOnlyEditInsideServiceInterface_KeepsDownstreamStepsCached()
    {
        var service1 = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(service1);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        // Edit: add a comment inside the interface body. Same shape, same symbols.
        var service1WithComment = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Demo.Svc
            {
                [ShaRpcService]
                public interface IFooService
                {
                    // comment-only change should not invalidate the model
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(service1, service1WithComment);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        // Services (the value-equal model) MUST be cached on trivia-only changes.
        AssertStepIsCachedOrUnchanged(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");

        // The downstream SourceOutput steps must produce cached outputs (no regeneration).
        AssertAllSourceOutputsCachedOrUnchanged(result);
    }

    [Fact]
    public void EditUnrelatedFile_DoesNotInvalidateServiceOrAggregateOrSourceOutputs()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var unrelatedTree = CSharpSyntaxTree.ParseText(UnrelatedDefault);

        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree, unrelatedTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        // Edit unrelated only.
        var unrelatedTree2 = CSharpSyntaxTree.ParseText("""
            namespace Demo.Other
            {
                public class Unrelated
                {
                    public int X { get; set; }
                    public int Y { get; set; } // new property unrelated to any service
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(unrelatedTree, unrelatedTree2);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        AssertStepIsCachedOrUnchanged(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");
        AssertAllSourceOutputsCachedOrUnchanged(result);
    }

    [Fact]
    public void AddingUnrelatedFile_DoesNotInvalidateExistingServiceOutputs()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var newUnrelated = CSharpSyntaxTree.ParseText("""
            namespace Demo.Brand.New
            {
                public class Added { public int N { get; set; } }
            }
            """);

        var compilation2 = compilation.AddSyntaxTrees(newUnrelated);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        AssertStepIsCachedOrUnchanged(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");
        AssertAllSourceOutputsCachedOrUnchanged(result);
    }

    [Fact]
    public void RenamingServiceMethod_InvalidatesServiceButKeepsAggregateCachedAndRegeneratesSources()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var renamed = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Demo.Svc
            {
                [ShaRpcService]
                public interface IFooService
                {
                    Task<int> SumAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        var compilation2 = compilation.ReplaceSyntaxTree(serviceTree, renamed);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        // Method shape changes must regenerate the service output without rebuilding
        // the extension aggregate, which only depends on service identity.
        AssertStepHasModifiedOutput(result, "Services");
        AssertStepIsCachedOrUnchanged(result, "AllServices");

        // Generated proxy must contain the new method name.
        var proxy = result.Results.Single().GeneratedSources
            .Single(g => g.HintName == "Demo_Svc_IFooService.ShaRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().Contain("SumAsync(int a, int b");
        proxy.Should().NotContain("AddAsync(");
    }

    [Fact]
    public void AddingSecondService_KeepsFirstServiceOutputCachedAndInvalidatesAggregate()
    {
        var service1 = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(service1);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var service2 = CSharpSyntaxTree.ParseText(Service2Default);
        var compilation2 = compilation.AddSyntaxTrees(service2);
        driver = driver.RunGenerators(compilation2);
        var result = driver.GetRunResult();

        // The first service's per-service Services step output (the value-equatable model)
        // must stay cached because IFooService didn't change. The AllServices aggregate
        // must be modified because the collection grew.
        var servicesOutputs = result.Results.Single().TrackedSteps["Services"]
            .SelectMany(s => s.Outputs)
            .ToArray();

        servicesOutputs.Should().HaveCount(2, "both services should flow through the Services step");

        // Find the first service's output by inspecting the value's InterfaceName via
        // reflection (the model type is internal to the generator assembly).
        var fooOutput = servicesOutputs.SingleOrDefault(o => InterfaceNameOf(o.Value) == "IFooService");
        fooOutput.Value.Should().NotBeNull("IFooService should still flow through the Services step");
        var cachedReasons = new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged };
        cachedReasons.Should().Contain(fooOutput.Reason,
            "IFooService's model is value-equal across runs and must be cached when a second, unrelated service is added");

        // The AllServices aggregate MUST be modified because the collection of services changed.
        AssertStepHasModifiedOutput(result, "AllServices");

        // Sanity: both services have generated files.
        var hints = result.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Demo_Svc_IFooService.ShaRpcProxy.g.cs");
        hints.Should().Contain("Demo_Svc2_IBarService.ShaRpcProxy.g.cs");
        hints.Should().Contain("ShaRpcExtensions.g.cs");
    }

    private static string? InterfaceNameOf(object? maybeModel)
    {
        if (maybeModel is null) return null;
        return maybeModel.GetType().GetProperty("InterfaceName")?.GetValue(maybeModel) as string;
    }

    // ---------- assertion helpers ----------

    private static void AssertStepIsCachedOrUnchanged(GeneratorDriverRunResult result, string trackingName)
    {
        var steps = result.Results.Single().TrackedSteps;
        steps.Should().ContainKey(trackingName, $"step '{trackingName}' should exist in tracked steps");

        var outputs = steps[trackingName].SelectMany(s => s.Outputs).ToArray();
        outputs.Should().NotBeEmpty($"step '{trackingName}' should have produced at least one output");

        outputs.Should().OnlyContain(
            o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged,
            $"step '{trackingName}' must remain cached/unchanged on this edit, but reasons were: {string.Join(", ", outputs.Select(o => o.Reason))}");
    }

    private static void AssertStepHasModifiedOutput(GeneratorDriverRunResult result, string trackingName)
    {
        var steps = result.Results.Single().TrackedSteps;
        steps.Should().ContainKey(trackingName);
        var outputs = steps[trackingName].SelectMany(s => s.Outputs).ToArray();
        outputs.Any(o => o.Reason == IncrementalStepRunReason.Modified
                      || o.Reason == IncrementalStepRunReason.New
                      || o.Reason == IncrementalStepRunReason.Removed)
            .Should().BeTrue($"step '{trackingName}' should have at least one Modified/New/Removed output, but reasons were: {string.Join(", ", outputs.Select(o => o.Reason))}");
    }

    private static void AssertAllSourceOutputsCachedOrUnchanged(GeneratorDriverRunResult result)
    {
        var allOutputs = result.Results.Single().TrackedOutputSteps
            .SelectMany(kvp => kvp.Value.Select(step => (StepName: kvp.Key, Step: step)))
            .SelectMany(t => t.Step.Outputs.Select(o => (t.StepName, o.Reason)))
            .ToArray();

        allOutputs.Should().NotBeEmpty("at least one source output should have been produced previously");

        var nonCached = allOutputs
            .Where(t => t.Reason != IncrementalStepRunReason.Cached && t.Reason != IncrementalStepRunReason.Unchanged)
            .ToArray();

        nonCached.Should().BeEmpty(
            "all source outputs must be cached/unchanged after a no-op edit, but got: " +
            string.Join(", ", nonCached.Select(x => x.StepName + "=" + x.Reason)));
    }
}
