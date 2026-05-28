using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class ReviewedIncrementalCollisionTests
{
    [Fact]
    public void RemovingExistingTypeCollision_RestoresServiceOutputs()
    {
        var colliding = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Incremental.Collision
            {
                public sealed class FooProxy { }

                [ShaRpcService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(colliding);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Should().Contain(d => d.Id == "SHARPC003");

        var fixedTree = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Incremental.Collision
            {
                [ShaRpcService]
                public interface IFoo
                {
                    int Get();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(colliding, fixedTree));
        var result = driver.GetRunResult();

        result.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003");
        result.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName == "Incremental_Collision_IFoo.ShaRpcProxy.g.cs");
    }

    [Fact]
    public void EditingDuplicateWireNameToUnique_RestoresServiceOutputs()
    {
        var duplicate = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Incremental.Wire
            {
                [ShaRpcService(Name = "same")]
                public interface IFoo
                {
                    int A();
                }

                [ShaRpcService(Name = "same")]
                public interface IBar
                {
                    int B();
                }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(duplicate);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        driver.GetRunResult().Diagnostics.Where(d => d.Id == "SHARPC003")
            .Should().HaveCount(2);

        var unique = CSharpSyntaxTree.ParseText("""
            using ShaRPC.Core.Attributes;

            namespace Incremental.Wire
            {
                [ShaRpcService(Name = "foo")]
                public interface IFoo
                {
                    int A();
                }

                [ShaRpcService(Name = "bar")]
                public interface IBar
                {
                    int B();
                }
            }
            """);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(duplicate, unique));
        var result = driver.GetRunResult();

        result.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003");
        var hints = result.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("Incremental_Wire_IFoo.ShaRpcProxy.g.cs");
        hints.Should().Contain("Incremental_Wire_IBar.ShaRpcProxy.g.cs");
        hints.Should().Contain("ShaRpcExtensions.g.cs");
    }
}
