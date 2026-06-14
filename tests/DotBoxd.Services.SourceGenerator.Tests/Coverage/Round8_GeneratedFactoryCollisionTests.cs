using System.Linq;
using DotBoxd.Services.SourceGenerator.Tests;
using Xunit;

namespace DotBoxd.Services.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 8 regression for the generated-type collision guard. The generator emits two assembly-level types
/// in <c>DotBoxd.Services.Generated</c>: <c>DotBoxdGeneratedExtensions</c> and the <c>DotBoxdGenerated</c> factory. Both
/// <c>ExistingTypeIndex.CanCollideWithGeneratedType</c> and
/// <c>GeneratedTypeCollisionValidator.ApplyPrimaryTypes</c> guard against a user-defined
/// <c>DotBoxdGeneratedExtensions</c> but were silent about <c>DotBoxdGenerated</c>: a user type of that name
/// produced a raw CS0101 with no explanatory DBXS003. The factory name must be guarded too.
/// </summary>
public sealed class Round8_GeneratedFactoryCollisionTests
{
    [Fact]
    public void ExistingGeneratedFactoryType_ProducesDBXS003_AndServicesAreSkipped()
    {
        const string source = """
            using DotBoxd.Services.Attributes;

            namespace DotBoxd.Services.Generated
            {
                public static class DotBoxdGenerated
                {
                }
            }

            namespace Regress.GeneratedFactoryCollision
            {
                [DotBoxdService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        // The collision with the generated factory must surface as DBXS003, not a raw CS0101.
        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS003" &&
                 d.GetMessage().Contains("factory type 'DotBoxd.Services.Generated.DotBoxdGenerated'"));

        // The service is skipped (mirrors the existing DotBoxdGeneratedExtensions collision test).
        Assert.DoesNotContain(
            runResult.Results.Single().GeneratedSources,
            g => g.HintName.Contains("IFoo."));
    }
}
