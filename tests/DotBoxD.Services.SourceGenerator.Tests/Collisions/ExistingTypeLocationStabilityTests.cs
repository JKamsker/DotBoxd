using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Collisions;

public class ExistingTypeLocationStabilityTests
{
    [Fact]
    public void DuplicateExistingTypeLocations_UseStablePathTieBreak()
    {
        const string collidingType = """
            namespace Regress.StableCollision
            {
                public sealed class FooProxy
                {
                }
            }
            """;
        var collidingB = CSharpSyntaxTree.ParseText(collidingType, path: "B.cs");
        var collidingA = CSharpSyntaxTree.ParseText(collidingType, path: "A.cs");
        var service = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;

            namespace Regress.StableCollision
            {
                [RpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """, path: "Service.cs");
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(collidingB, service, collidingA);

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();

        var diagnostic = runResult.Diagnostics.Single(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated proxy type 'FooProxy' would collide"));
        diagnostic.Location.GetLineSpan().Path.Should().Be("A.cs");
    }
}
