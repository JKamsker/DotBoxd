namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// Round 8 regression for the generated-type collision guard. The generator emits two assembly-level types
/// in <c>DotBoxD.Services.Generated</c>: <c>DotBoxDGeneratedExtensions</c> and the <c>DotBoxDGenerated</c> factory. Both
/// <c>ExistingTypeIndex.CanCollideWithGeneratedType</c> and
/// <c>GeneratedTypeCollisionValidator.ApplyPrimaryTypes</c> guard against a user-defined
/// <c>DotBoxDGeneratedExtensions</c> but were silent about <c>DotBoxDGenerated</c>: a user type of that name
/// produced a raw CS0101 with no explanatory DBXS003. The factory name must be guarded too.
/// </summary>
public sealed class Round8_GeneratedFactoryCollisionTests
{
    [Fact]
    public void ExistingGeneratedFactoryType_ProducesDBXS003_AndServicesAreSkipped()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGenerated
                {
                }
            }

            namespace Regress.GeneratedFactoryCollision
            {
                [RpcService]
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
                 d.GetMessage().Contains("factory type 'DotBoxD.Services.Generated.DotBoxDGenerated'"));

        // The service is skipped (mirrors the existing DotBoxDGeneratedExtensions collision test).
        Assert.DoesNotContain(
            runResult.Results.Single().GeneratedSources,
            g => g.HintName.Contains("IFoo."));
    }
}
