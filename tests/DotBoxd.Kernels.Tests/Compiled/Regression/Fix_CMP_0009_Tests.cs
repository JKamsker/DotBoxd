using System.Text.RegularExpressions;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for CMP-0009: the release-readiness checklist marks the
/// <c>Error code reference</c> as complete, but the docs do not contain a maintained
/// per-code reference that documents every <see cref="SandboxErrorCode"/> value with
/// operational guidance (safe user message, likely causes, retryability, audit/event
/// expectations, and admin escalation notes).
///
/// The spec only repeats the bare enum listing (see
/// <c>spec/16-public-api.md</c>) and the binding/runtime sections cover a subset of
/// codes, so operators and SDK consumers have no stable per-code behavior reference.
///
/// These tests pin the documentation contract the checklist promises. They are red
/// until a dedicated per-code error reference ships under <c>docs/</c> that:
/// <list type="bullet">
///   <item>names every current <see cref="SandboxErrorCode"/> value, and</item>
///   <item>supplies the per-code operational guidance vocabulary the finding requires.</item>
/// </list>
/// The tests only use the already-public <see cref="SandboxErrorCode"/> enum and the
/// repository file tree, so they compile against the current codebase.
/// </summary>
public sealed class Fix_CMP_0009_Tests
{
    // The operational guidance vocabulary every per-code reference must establish so a
    // bare enum listing cannot satisfy the gate. Each marker is matched case-insensitively.
    private static readonly string[] RequiredGuidanceMarkers =
    [
        "retry",        // retry / retryability guidance
        "audit",        // audit / event expectations
        "escalat",      // admin escalation notes ("escalate"/"escalation")
        "safe message", // tenant-safe user message guidance
    ];

    [Fact]
    public void A_maintained_error_code_reference_documents_every_sandbox_error_code()
    {
        var codeNames = Enum.GetNames<SandboxErrorCode>();
        Assert.NotEmpty(codeNames);

        var reference = FindErrorCodeReference();

        Assert.True(
            reference is not null,
            "Expected a maintained per-code error reference under docs/ that names every " +
            "SandboxErrorCode value with operational guidance (retry, audit/event, admin " +
            "escalation, and safe-message notes), but no such document was found. CMP-0009: the " +
            "release checklist marks 'Error code reference' complete without one.");

        var (path, text) = reference!.Value;

        var missing = codeNames
            .Where(name => !Regex.IsMatch(
                text,
                $@"\b{Regex.Escape(name)}\b",
                RegexOptions.None))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Error code reference '{path}' is missing entries for: {string.Join(", ", missing)}. " +
            "Every SandboxErrorCode value must have its own documented entry so new enum values " +
            "cannot ship undocumented.");
    }

    [Fact]
    public void Error_code_reference_provides_per_code_operational_guidance()
    {
        var reference = FindErrorCodeReference();

        Assert.True(
            reference is not null,
            "No maintained per-code error reference was found under docs/, so the required " +
            "operational guidance (retry, audit/event, admin escalation, safe message) is absent.");

        var (path, text) = reference!.Value;
        var lower = text.ToLowerInvariant();

        var missingMarkers = RequiredGuidanceMarkers
            .Where(marker => !lower.Contains(marker))
            .ToArray();

        Assert.True(
            missingMarkers.Length == 0,
            $"Error code reference '{path}' omits required operational guidance markers: " +
            $"{string.Join(", ", missingMarkers)}. A reference must cover retryability, audit/event " +
            "expectations, admin escalation, and tenant-safe messaging per code.");
    }

    /// <summary>
    /// Finds a documentation file that is a genuine per-code error reference: it must name
    /// the full current enum taxonomy and use the operational guidance vocabulary. The
    /// agent-loop working area is excluded so finding/queue markdown that merely lists the
    /// codes cannot satisfy the gate.
    /// </summary>
    private static (string Path, string Text)? FindErrorCodeReference()
    {
        var docsRoot = Path.Combine(RepositoryRoot(), "docs");
        if (!Directory.Exists(docsRoot))
        {
            return null;
        }

        var agentLoop = Path.Combine(docsRoot, "agent-loop") + Path.DirectorySeparatorChar;
        var codeNames = Enum.GetNames<SandboxErrorCode>();

        foreach (var file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
        {
            if (file.StartsWith(agentLoop, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var lower = text.ToLowerInvariant();

            var namesEveryCode = codeNames.All(name =>
                Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b", RegexOptions.None));
            var hasGuidance = RequiredGuidanceMarkers.All(marker => lower.Contains(marker));

            if (namesEveryCode && hasGuidance)
            {
                return (file, text);
            }
        }

        return null;
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
