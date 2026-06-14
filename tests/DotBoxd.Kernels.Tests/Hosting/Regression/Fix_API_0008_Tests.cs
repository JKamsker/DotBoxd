using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using DotBoxd.Plugins.Analyzer;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0008: the DotBoxd.Plugins.Analyzer package ships stable
/// <c>SGP</c> diagnostic IDs, so every shipped/unshipped rule must expose a public
/// <see cref="DiagnosticDescriptor.HelpLinkUri"/> that deep-links to its release-tracking
/// reference. Without it, IDE/build output is the only documentation a NuGet consumer has.
/// </summary>
public sealed class Fix_API_0008_Tests
{
    [Fact]
    public void Forbidden_host_api_rule_links_to_shipped_reference()
    {
        var rule = DotBoxdPluginAnalyzer.ForbiddenHostApiRule;

        Assert.Equal("DBXK001", rule.Id);
        AssertHelpLink(rule, "AnalyzerReleases.Shipped.md", "DBXK001");
    }

    [Fact]
    public void Live_setting_type_rule_links_to_shipped_reference()
    {
        var rule = DotBoxdPluginAnalyzer.LiveSettingTypeRule;

        Assert.Equal("DBXK020", rule.Id);
        AssertHelpLink(rule, "AnalyzerReleases.Shipped.md", "DBXK020");
    }

    [Fact]
    public void Every_supported_diagnostic_exposes_a_help_link_for_its_id()
    {
        var analyzer = new DotBoxdPluginAnalyzer();

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
            root, "src", "CodeGeneration", "DotBoxd.Plugins.Analyzer", "AnalyzerReleases.Shipped.md"));
        var unshipped = File.ReadAllText(Path.Combine(
            root, "src", "CodeGeneration", "DotBoxd.Plugins.Analyzer", "AnalyzerReleases.Unshipped.md"));

        // The help links published on the analyzer rules point consumers at these files,
        // so each tracked ID must actually appear in the reference it links to.
        Assert.Contains("DBXK001", shipped);
        Assert.Contains("DBXK020", shipped);
        Assert.Contains("DBXK100", unshipped);
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
            if (File.Exists(Path.Combine(current.FullName, "DotBoxd.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
