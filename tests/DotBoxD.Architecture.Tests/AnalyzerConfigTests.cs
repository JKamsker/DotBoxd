namespace DotBoxD.Architecture.Tests;

/// <summary>
/// Regression guard for the static-analysis configuration: a few key rule severities and the
/// analyzer wiring must not be silently weakened or removed. Quality gates are only as durable as
/// the config behind them, so the config itself is under test.
/// </summary>
public sealed class AnalyzerConfigTests
{
    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(ArchTestSupport.RepositoryRoot(), relativePath));

    [Theory]
    // Enforced (warning => CI error): the two pillars of the correctness/security set.
    [InlineData("dotnet_diagnostic.MA0004.severity = warning")]
    [InlineData("dotnet_diagnostic.RCS1075.severity = warning")]
    // Deliberately disabled rules whose rationale would be lost if a future edit silently re-enabled
    // them (each conflicts with an intentional, tested convention in this codebase).
    [InlineData("dotnet_diagnostic.MA0006.severity = none")]
    [InlineData("dotnet_diagnostic.MA0012.severity = none")]
    [InlineData("dotnet_diagnostic.MA0015.severity = none")]
    public void EditorConfig_pins_curated_analyzer_severities(string expected)
        => Assert.Contains(expected, Read(".editorconfig"), StringComparison.Ordinal);

    [Fact]
    public void Code_style_enforcement_stays_on_in_the_build()
        => Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", Read("Directory.Build.props"), StringComparison.Ordinal);

    [Theory]
    [InlineData("Roslynator.Analyzers")]
    [InlineData("Meziantou.Analyzer")]
    public void Static_analysis_packages_remain_referenced(string package)
    {
        Assert.Contains(package, Read("Directory.Packages.props"), StringComparison.Ordinal);
        Assert.Contains(package, Read("Directory.Build.props"), StringComparison.Ordinal);
    }
}
