using System.Linq;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 8 regression for the generated-type collision guard. The generator emits two assembly-level types
/// in <c>ShaRPC.Generated</c>: <c>ShaRpcGeneratedExtensions</c> and the <c>ShaRpcGenerated</c> factory. Both
/// <c>ExistingTypeIndex.CanCollideWithGeneratedType</c> and
/// <c>GeneratedTypeCollisionValidator.ApplyPrimaryTypes</c> guard against a user-defined
/// <c>ShaRpcGeneratedExtensions</c> but were silent about <c>ShaRpcGenerated</c>: a user type of that name
/// produced a raw CS0101 with no explanatory SHARPC003. The factory name must be guarded too.
/// </summary>
public sealed class Round8_GeneratedFactoryCollisionTests
{
    [Fact]
    public void ExistingGeneratedFactoryType_ProducesSHARPC003_AndServicesAreSkipped()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace ShaRPC.Generated
            {
                public static class ShaRpcGenerated
                {
                }
            }

            namespace Regress.GeneratedFactoryCollision
            {
                [ShaRpcService]
                public interface IFoo
                {
                    int Bar();
                }
            }
            """;

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        // The collision with the generated factory must surface as SHARPC003, not a raw CS0101.
        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "SHARPC003" &&
                 d.GetMessage().Contains("factory type 'ShaRPC.Generated.ShaRpcGenerated'"));

        // The service is skipped (mirrors the existing ShaRpcGeneratedExtensions collision test).
        Assert.DoesNotContain(
            runResult.Results.Single().GeneratedSources,
            g => g.HintName.Contains("IFoo."));
    }
}
