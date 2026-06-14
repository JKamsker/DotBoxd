using System.Text.RegularExpressions;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for CMP-0008: the release readiness checklist marks every
/// <c>Documentation</c> inventory item complete (<c>[x]</c>), but
/// <c>scripts/check-release-readiness.ps1</c> only builds evidence maps for the required
/// MVP/compiled-mode release items and for the security-review sections. The completed
/// documentation inventory items (user-facing language docs, capability catalog, error code
/// reference, debugging guide, operational runbook, ...) have <em>no</em> evidence coverage,
/// so documentation can drift or disappear without the script detecting that the checked
/// entries are no longer supported.
///
/// These tests pin the correct behavior the fix must deliver: the completed documentation
/// inventory items must be covered by an evidence map / evidence references in the release
/// readiness script, exactly as the required release items and security-review sections
/// already are. They read only files that exist today and assert on script/checklist text, so
/// they compile against the current tree and fail until the documentation evidence coverage is
/// added.
/// </summary>
public sealed class Fix_CMP_0008_Tests
{
    private const string ReleaseReadinessRelative =
        "docs/Specs/Initial/safe-ir-sandbox-spec/checklists/release-readiness.md";

    private const string ScriptRelative = "scripts/check-release-readiness.ps1";

    [Fact]
    public void Documentation_section_is_inventory_and_fully_checked_complete()
    {
        // Sanity anchor for the finding: the Documentation section is a release-gate: inventory
        // section whose items are all marked complete. If this ever stops being true the rest of
        // the assertions would be vacuous, so pin it explicitly.
        var completed = CompletedDocumentationItems();

        Assert.NotEmpty(completed);
        // The known completed inventory documentation items the finding calls out by name.
        Assert.Contains("User-facing language docs.", completed);
        Assert.Contains("Capability catalog.", completed);
        Assert.Contains("Error code reference.", completed);
        Assert.Contains("Debugging guide.", completed);
        Assert.Contains("Operational runbook.", completed);
    }

    [Fact]
    public void Release_readiness_script_covers_completed_documentation_items_with_evidence()
    {
        var script = ReadRepositoryText(ScriptRelative);
        var completed = CompletedDocumentationItems();

        // The script must establish documentation evidence coverage the same way it already does
        // for the required release items ($releaseEvidence) and security-review sections
        // ($securitySectionEvidence). Today there is no documentation evidence map at all, so the
        // completed inventory documentation items go entirely unverified.
        Assert.True(
            HasDocumentationEvidenceCoverage(script),
            "check-release-readiness.ps1 does not verify evidence for the completed " +
            "Documentation inventory items. Completed documentation checklist entries must be " +
            "backed by an evidence map or explicit evidence links so the checklist cannot " +
            "overstate release completeness when docs drift or disappear.");

        // Each completed documentation item must be referenced by the script's coverage so a
        // checked entry without any evidence mapping fails the readiness check.
        var uncovered = completed
            .Where(text => !script.Contains(text, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            uncovered.Count == 0,
            "Completed Documentation inventory items have no evidence reference in " +
            "check-release-readiness.ps1: " + string.Join("; ", uncovered));
    }

    /// <summary>
    /// Detects whether the script wires any documentation evidence coverage for the completed
    /// inventory items. The fix may name the map differently, so this looks for the structural
    /// signal (a documentation-focused evidence map keyed by the Documentation section) rather
    /// than a single hard-coded symbol. Today none of these signals are present.
    /// </summary>
    private static bool HasDocumentationEvidenceCoverage(string script)
    {
        var mentionsDocumentationSection =
            Regex.IsMatch(script, @"""Documentation""", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(script, @"\$documentation", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(script, @"docEvidence", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(script, @"documentationEvidence", RegexOptions.IgnoreCase);

        // The coverage must point completed documentation items at concrete docs. At least one of
        // the named documentation items must appear in the script so the evidence is traceable.
        var referencesADocumentationItem =
            script.Contains("language docs", StringComparison.OrdinalIgnoreCase) ||
            script.Contains("Capability catalog", StringComparison.OrdinalIgnoreCase) ||
            script.Contains("Error code reference", StringComparison.OrdinalIgnoreCase) ||
            script.Contains("Debugging guide", StringComparison.OrdinalIgnoreCase) ||
            script.Contains("Operational runbook", StringComparison.OrdinalIgnoreCase);

        return mentionsDocumentationSection && referencesADocumentationItem;
    }

    private static IReadOnlyList<string> CompletedDocumentationItems()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), ReleaseReadinessRelative));
        var items = new List<string>();
        var inDocumentationSection = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            var heading = Regex.Match(line, @"^##\s+(.+)$");
            if (heading.Success)
            {
                inDocumentationSection =
                    string.Equals(heading.Groups[1].Value.Trim(), "Documentation", StringComparison.Ordinal);
                continue;
            }

            if (!inDocumentationSection)
            {
                continue;
            }

            var item = Regex.Match(line, @"^- \[([ xX])\] (.+)$");
            if (item.Success && item.Groups[1].Value is "x" or "X")
            {
                items.Add(item.Groups[2].Value.Trim());
            }
        }

        return items;
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
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
