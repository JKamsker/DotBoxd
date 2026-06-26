using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncDtoConstructorSelectionRegressionTests
{
    [Fact]
    public void Return_dto_prefers_smaller_reconstructible_constructor_over_larger_unusable_match()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Profile
            {
                public Profile(int code) => Code = code;

                public Profile(int rank, int score) => Rank = rank;

                public int Code { get; }
                public int Rank { get; set; }
                public int Score => Code + Rank;
            }

            public static ValueTask<Profile> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Profile(world.GetHealth("monster-1")) { Rank = 9 };
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains(
            "new global::Sample.Usage.Profile(value.GetItem(0).Int32Value)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("@Rank =", source, StringComparison.Ordinal);
    }
}
