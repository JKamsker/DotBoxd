using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

/// <summary>
/// Regression coverage for API-0008: the DotBoxD.Plugins.Analyzer package ships stable
/// <c>DBXK</c> diagnostic IDs, so every shipped/unshipped rule must expose a public
/// <see cref="DiagnosticDescriptor.HelpLinkUri"/> that deep-links to its release-tracking
/// reference. Without it, IDE/build output is the only documentation a NuGet consumer has.
/// </summary>
public sealed class Fix_API_0008_Tests
{
    private const string ExpectedHelpLinkPrefix =
        "https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Plugins.Analyzer/";

    [Fact]
    public void Forbidden_host_api_rule_links_to_shipped_reference()
    {
        var rule = DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer.ForbiddenHostApiRule;

        Assert.Equal("DBXK001", rule.Id);
        AssertHelpLink(rule, "AnalyzerReleases.Shipped.md", "DBXK001");
    }

    [Fact]
    public void Live_setting_type_rule_links_to_shipped_reference()
    {
        var rule = DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer.LiveSettingTypeRule;

        Assert.Equal("DBXK020", rule.Id);
        AssertHelpLink(rule, "AnalyzerReleases.Shipped.md", "DBXK020");
    }

    [Fact]
    public void Every_plugin_diagnostic_exposes_a_help_link_for_its_id()
    {
        var rules = AllPluginDiagnosticDescriptors().ToArray();

        Assert.NotEmpty(rules);
        foreach (var rule in rules)
        {
            AssertHelpLink(rule, "AnalyzerReleases", rule.Id);
        }
    }

    [Fact]
    public void Release_tracked_rule_ids_are_documented_in_the_referenced_files()
    {
        var root = RepositoryRoot();
        var shipped = File.ReadAllText(Path.Combine(
            root, "src", "CodeGeneration", "DotBoxD.Plugins.Analyzer", "AnalyzerReleases.Shipped.md"));
        var unshipped = File.ReadAllText(Path.Combine(
            root, "src", "CodeGeneration", "DotBoxD.Plugins.Analyzer", "AnalyzerReleases.Unshipped.md"));

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
        Assert.StartsWith(ExpectedHelpLinkPrefix, rule.HelpLinkUri, StringComparison.Ordinal);
        Assert.DoesNotContain("Safe-IR", rule.HelpLinkUri, StringComparison.Ordinal);
        Assert.EndsWith("#" + expectedRuleId, rule.HelpLinkUri);
    }

    private static IEnumerable<DiagnosticDescriptor> AllPluginDiagnosticDescriptors()
    {
        var seen = new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal);
        var analyzer = new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer();
        foreach (var rule in analyzer.SupportedDiagnostics)
        {
            if (seen.TryGetValue(rule.Id, out var existing))
            {
                AssertEquivalentDescriptor(existing, rule);
                continue;
            }

            seen.Add(rule.Id, rule);
            yield return rule;
        }

        foreach (var field in typeof(DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzerDiagnostics).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (field.FieldType == typeof(DiagnosticDescriptor) &&
                field.GetValue(null) is DiagnosticDescriptor rule)
            {
                if (seen.TryGetValue(rule.Id, out var existing))
                {
                    AssertEquivalentDescriptor(existing, rule);
                    continue;
                }

                seen.Add(rule.Id, rule);
                yield return rule;
            }
        }
    }

    private static void AssertEquivalentDescriptor(DiagnosticDescriptor expected, DiagnosticDescriptor actual)
    {
        Assert.Equal(expected.HelpLinkUri, actual.HelpLinkUri);
        Assert.Equal(expected.Title.ToString(), actual.Title.ToString());
        Assert.Equal(expected.MessageFormat.ToString(), actual.MessageFormat.ToString());
        Assert.Equal(expected.Description.ToString(), actual.Description.ToString());
        Assert.Equal(expected.Category, actual.Category);
        Assert.Equal(expected.DefaultSeverity, actual.DefaultSeverity);
        Assert.Equal(expected.IsEnabledByDefault, actual.IsEnabledByDefault);
        Assert.Equal(expected.CustomTags, actual.CustomTags);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
