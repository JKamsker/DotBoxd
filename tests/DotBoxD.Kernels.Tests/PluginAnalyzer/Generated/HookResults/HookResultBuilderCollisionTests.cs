using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultBuilderCollisionTests
{
    [Fact]
    public void Author_builder_name_overloads_do_not_suppress_non_colliding_generated_builders()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public static DamageResult Ok(int damage) => new() { Success = true, Damage = damage };
                public static DamageResult Reject() => new() { Success = false, Reason = "manual" };
                public DamageResult WithDamage() => this with { Damage = 999 };
            }
            """;

        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(source));

        Assert.Contains("public static DamageResult Ok()", generated, StringComparison.Ordinal);
        Assert.Contains("public static DamageResult Reject(string? reason = null)", generated, StringComparison.Ordinal);
        Assert.Contains("public DamageResult WithDamage(int damage)", generated, StringComparison.Ordinal);
    }
}
