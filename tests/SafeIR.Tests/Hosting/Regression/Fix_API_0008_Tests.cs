using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SafeIR.PluginAnalyzer;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for API-0008: the SafeIR.PluginAnalyzer package ships stable
/// <c>SGP</c> diagnostic IDs, so every shipped/unshipped rule must expose a public
/// <see cref="DiagnosticDescriptor.HelpLinkUri"/> that deep-links to its release-tracking
/// reference. Without it, IDE/build output is the only documentation a NuGet consumer has.
/// </summary>
public sealed class Fix_API_0008_Tests
{
    [Fact]
    public void Forbidden_host_api_rule_links_to_shipped_reference()
    {
        var rule = SafeIrPluginAnalyzer.ForbiddenHostApiRule;

        Assert.Equal("SGP001", rule.Id);
        AssertHelpLink(rule, "AnalyzerReleases.Shipped.md", "SGP001");
    }

    [Fact]
    public void Live_setting_type_rule_links_to_shipped_reference()
    {
        var rule = SafeIrPluginAnalyzer.LiveSettingTypeRule;

        Assert.Equal("SGP020", rule.Id);
        AssertHelpLink(rule, "AnalyzerReleases.Shipped.md", "SGP020");
    }

    [Fact]
    public void Every_supported_diagnostic_exposes_a_help_link_for_its_id()
    {
        var analyzer = new SafeIrPluginAnalyzer();

        Assert.NotEmpty(analyzer.SupportedDiagnostics);
        foreach (var rule in analyzer.SupportedDiagnostics)
        {
            AssertHelpLink(rule, "AnalyzerReleases", rule.Id);
        }
    }

    [Fact]
    public void Release_tracked_rule_ids_are_documented_in_the_referenced_files()
    {
        var root = RepositoryRoot();
        var shipped = File.ReadAllText(Path.Combine(
            root, "src", "SafeIR.PluginAnalyzer", "AnalyzerReleases.Shipped.md"));
        var unshipped = File.ReadAllText(Path.Combine(
            root, "src", "SafeIR.PluginAnalyzer", "AnalyzerReleases.Unshipped.md"));

        // The help links published on the analyzer rules point consumers at these files,
        // so each tracked ID must actually appear in the reference it links to.
        Assert.Contains("SGP001", shipped);
        Assert.Contains("SGP020", shipped);
        Assert.Contains("SGP100", unshipped);
    }

    private static void AssertHelpLink(
        DiagnosticDescriptor rule,
        string expectedReferenceFile,
        string expectedRuleId)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(rule.HelpLinkUri),
            $"Rule {rule.Id} must expose a public help link for package consumers.");
        Assert.True(
            Uri.TryCreate(rule.HelpLinkUri, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps,
            $"Rule {rule.Id} help link must be an absolute https URI, was '{rule.HelpLinkUri}'.");
        Assert.Contains(expectedReferenceFile, rule.HelpLinkUri);
        Assert.EndsWith("#" + expectedRuleId, rule.HelpLinkUri);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SafeIR.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
