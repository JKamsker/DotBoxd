using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class ReviewedIncrementalCollisionTests
{
    [Fact]
    public void RemovingExistingTypeCollision_RestoresServiceOutputs()
    {
        var colliding = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Collision
            {
                public sealed class FooProxy { }

                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(colliding);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Should().Contain(d => d.Id == "DBXS003");

        var fixedTree = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Collision
            {
                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(colliding, fixedTree));
        var result = driver.GetRunResult();

        result.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        result.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName == "Incremental_Collision_IFoo.DotBoxdRpcProxy.g.cs");
    }

    [Fact]
    public void EditingDuplicateWireNameToUnique_RestoresServiceOutputs()
    {
        var duplicate = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Wire
            {
                [DotBoxdService(Name = "same")]
                public interface IFoo
                {
                    int A();
                }

                [DotBoxdService(Name = "same")]
                public interface IBar
                {
                    int B();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(duplicate);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2);

        var unique = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Wire
            {
                [DotBoxdService(Name = "foo")]
                public interface IFoo
                {
                    int A();
                }

                [DotBoxdService(Name = "bar")]
                public interface IBar
                {
                    int B();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(duplicate, unique));
        var result = driver.GetRunResult();

        result.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        var hints = result.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Incremental_Wire_IFoo.DotBoxdRpcProxy.g.cs");
        hints.Should().Contain("Incremental_Wire_IBar.DotBoxdRpcProxy.g.cs");
        hints.Should().Contain("DotBoxdRpcExtensions.g.cs");
    }

    [Fact]
    public void LocationOnlyEditAroundExistingCollision_DoesNotInvalidateExistingTypeKeys()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Location
            {
                public sealed class FooProxy { }

                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var moved = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Location
            {
                // line-only move for the colliding type
                public sealed class FooProxy { }

                [DotBoxdService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, moved));
        var result = driver.GetRunResult();

        StepReasons(result, "ExistingTypeKeys").Should().OnlyContain(r => IsCachedOrUnchanged(r));
        StepReasons(result, "ExistingTypes").Should().OnlyContain(r => IsCachedOrUnchanged(r));
        StepReasons(result, "ExistingTypeDeclarations")
            .Should().Contain(IncrementalStepRunReason.Modified);
        StepReasons(result, "ExistingTypeLocations")
            .Should().Contain(IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void AddingCollisionForOneService_KeepsUnrelatedServiceCached()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Mixed
            {
                [DotBoxdService(Name = "foo")]
                public interface IFoo
                {
                    int Get();
                }

                [DotBoxdService(Name = "stable")]
                public interface IStable
                {
                    int Get();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var colliding = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.Mixed
            {
                public sealed class FooProxy { }

                [DotBoxdService(Name = "foo")]
                public interface IFoo
                {
                    int Get();
                }

                [DotBoxdService(Name = "stable")]
                public interface IStable
                {
                    int Get();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, colliding));
        var result = driver.GetRunResult();

        result.Diagnostics.Should().Contain(d => d.Id == "DBXS003");
        StepReasons(result, "ExistingTypes").Should().Contain(IncrementalStepRunReason.Modified);
        StepReasons(result, "RejectedServices").Should().Contain(IncrementalStepRunReason.Modified);

        var stableOutput = result.Results.Single().TrackedSteps["Services"]
            .SelectMany(s => s.Outputs)
            .Single(o => InterfaceNameOf(o.Value) == "IStable");
        IsCachedOrUnchanged(stableOutput.Reason).Should().BeTrue();
        result.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName == "Incremental_Mixed_IStable.DotBoxdRpcProxy.g.cs");
    }

    [Fact]
    public void EditingGeneratedServiceNameCollisionToUnique_RestoresServiceOutputs()
    {
        var duplicate = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.GeneratedName
            {
                [DotBoxdService(Name = "ifoo")]
                public interface IFoo
                {
                    int A();
                }

                [DotBoxdService(Name = "foo")]
                public interface Foo
                {
                    int B();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(duplicate);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2);

        var unique = CSharpSyntaxTree.ParseText("""
            using DotBoxd.Services.Attributes;

            namespace Incremental.GeneratedName
            {
                [DotBoxdService(Name = "ifoo")]
                public interface IFoo
                {
                    int A();
                }

                [DotBoxdService(Name = "bar")]
                public interface IBar
                {
                    int B();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(duplicate, unique));
        var result = driver.GetRunResult();

        result.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        StepReasons(result, "GeneratedServiceNames")
            .Should().Contain(IncrementalStepRunReason.Modified);
        var hints = result.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Incremental_GeneratedName_IFoo.DotBoxdRpcProxy.g.cs");
        hints.Should().Contain("Incremental_GeneratedName_IBar.DotBoxdRpcProxy.g.cs");
    }

    private static IncrementalStepRunReason[] StepReasons(
        GeneratorDriverRunResult result,
        string trackingName) =>
        result.Results.Single().TrackedSteps[trackingName]
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToArray();

    private static bool IsCachedOrUnchanged(IncrementalStepRunReason reason) =>
        reason == IncrementalStepRunReason.Cached ||
        reason == IncrementalStepRunReason.Unchanged;

    private static string? InterfaceNameOf(object? maybeModel)
    {
        if (maybeModel is null)
        {
            return null;
        }

        return maybeModel.GetType().GetProperty("InterfaceName")?.GetValue(maybeModel) as string;
    }
}
