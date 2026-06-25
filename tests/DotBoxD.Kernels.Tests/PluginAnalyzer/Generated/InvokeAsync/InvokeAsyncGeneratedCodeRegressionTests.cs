using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedCodeRegressionTests
{
    [Fact]
    public void Anonymous_return_type_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static async ValueTask<int> Run(RemotePluginServer kernels)
            {
                var value = await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new { Health = world.GetHealth("monster-1") };
                });
                return value.Health;
            }
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("anonymous", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_nested_return_type_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            private sealed record Secret(int Health);

            public static async ValueTask<int> Run(RemotePluginServer kernels)
            {
                var value = await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Secret(world.GetHealth("monster-1"));
                });
                return value.Health;
            }
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("accessible from generated code", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_nested_capture_bag_type_reports_InvokeAsync_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            private sealed class SecretCapture
            {
                public int Health { get; set; }
            }

            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                var captures = new SecretCapture();
                return kernels.InvokeAsync(captures, async (IGameWorldAccess world, SecretCapture bag) =>
                {
                    return bag.Health + world.GetHealth("monster-1");
                });
            }
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("accessible from generated code", StringComparison.Ordinal));
    }

    [Fact]
    public void Computed_get_only_return_dto_generates_compilable_reader()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public readonly record struct Point(int X, int Y)
            {
                public int Sum => X + Y;
            }

            public static ValueTask<Point> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Point(world.GetHealth("monster-1"), 4);
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Settable_return_dto_without_matching_constructor_generates_compilable_reader()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Profile
            {
                public int Health { get; set; }
                public string Name { get; init; } = "";
            }

            public static ValueTask<Profile> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Profile { Health = world.GetHealth("monster-1"), Name = "hero" };
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Constructor_and_settable_return_dto_generates_compilable_reader()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Profile
            {
                public Profile()
                {
                }

                public Profile(int Health) => this.Health = Health;

                public Profile(int Health, int Rank)
                {
                    this.Health = Health;
                    this.Rank = Rank;
                }

                public int Health { get; set; }
                public int Rank { get; set; }
                public string Name { get; set; } = "";
            }

            public static ValueTask<Profile> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Profile { Health = world.GetHealth("monster-1"), Rank = 9, Name = "hero" };
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains(
            "new global::Sample.Usage.Profile(value.GetItem(0).Int32Value, value.GetItem(1).Int32Value)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("@Name =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Keyword_capture_member_generates_compilable_writer_and_sync_out()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public int @event { get; set; }
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.@event = world.GetHealth("monster-1");
                    return bag.@event;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("captures.@event =", source, StringComparison.Ordinal);
        Assert.Contains("captures.@event", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Float_and_wide_enum_results_generate_compilable_readers()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public enum Wide : long
            {
                High = 5_000_000_000L
            }

            public static async ValueTask<float> FloatRun(RemotePluginServer kernels)
                => await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return 1.5f;
                });

            public static ValueTask<Wide> EnumRun(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return Wide.High;
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }
}
