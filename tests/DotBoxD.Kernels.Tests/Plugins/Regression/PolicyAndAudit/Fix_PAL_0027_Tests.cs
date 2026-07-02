using System.Reflection;
using System.Text.RegularExpressions;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.PolicyAndAudit;

/// <summary>
/// Regression coverage for PAL-0027: the audit sanitizer clones and regex-scans
/// log messages even when they are already clean (no control characters and no
/// secret markers). A clean message has nothing to sanitize or redact, so the
/// sanitizer should take a fast path and return the ORIGINAL string instance
/// instead of allocating a full-length clone via ToCharArray + new string(...).
///
/// These tests assert the concrete observable consequence of the missing fast
/// path: for a clean message the returned reference must be the same instance as
/// the input. Today <see cref="AuditTextSanitizer.SanitizeAndRedact"/> always
/// rebuilds the string, so the returned reference differs and these tests fail.
/// </summary>
public sealed class Fix_PAL_0027_Tests
{
    [Fact]
    public void SanitizeAndRedact_returns_same_instance_for_clean_short_message()
    {
        // Arrange: build a runtime (non-interned) string so reference identity is
        // meaningful. The content is clean: no control chars, no secret markers.
        var clean = new string("operation completed successfully".ToCharArray());

        // Act
        var result = AuditTextSanitizer.SanitizeAndRedact(clean);

        // Assert: content is unchanged (sanity) and no allocation/clone occurred.
        Assert.Equal(clean, result);
        Assert.Same(clean, result);
    }

    [Fact]
    public void SanitizeAndRedact_returns_same_instance_for_clean_large_message()
    {
        // Arrange: a clean ~4 KB message dominated by ordinary operational text.
        var clean = new string('a', 4096);

        // Act
        var result = AuditTextSanitizer.SanitizeAndRedact(clean);

        // Assert
        Assert.Equal(clean, result);
        Assert.Same(clean, result);
    }

    [Fact]
    public void SanitizeAndRedact_returns_same_instance_for_clean_message_without_secret_markers()
    {
        // Arrange: punctuation and digits but nothing resembling a credential,
        // URI userinfo, authorization header, or secret key/value pair.
        var clean = new string("user 42 finished batch job #17 in 1234 ms (ok)".ToCharArray());

        // Act
        var result = AuditTextSanitizer.SanitizeAndRedact(clean);

        // Assert
        Assert.Equal(clean, result);
        Assert.Same(clean, result);
    }

    [Fact]
    public void RedactPathSegments_returns_same_instance_for_clean_path()
    {
        var clean = new string("/v1/config/public/status".ToCharArray());

        var result = AuditTextSanitizer.RedactPathSegments(clean);

        Assert.Equal(clean, result);
        Assert.Same(clean, result);
    }

    [Fact]
    public void RedactPathSegments_still_redacts_direct_secret_marker_and_value()
    {
        var result = AuditTextSanitizer.RedactPathSegments("/v1/token/abc123/status");

        Assert.Equal("/v1/[redacted]/[redacted]/status", result);
    }

    [Fact]
    public void RedactPathSegments_still_redacts_percent_encoded_secret_marker_and_value()
    {
        var result = AuditTextSanitizer.RedactPathSegments("/v1/%74%6f%6b%65%6e/abc123/status");

        Assert.Equal("/v1/[redacted]/[redacted]/status", result);
    }

    [Fact]
    public void RedactPathSegments_prefilter_preserves_regex_unicode_case_folding()
    {
        var result = AuditTextSanitizer.RedactPathSegments("/v1/\u212Aey=cleanvalue/status");

        Assert.Equal("/v1/[redacted]/status", result);
    }

    [Fact]
    public void RedactPathSegments_standalone_marker_preserves_regex_unicode_case_folding()
    {
        var result = AuditTextSanitizer.RedactPathSegments("/v1/\u212Aey/abc123/status");

        Assert.Equal("/v1/[redacted]/[redacted]/status", result);
    }

    [Theory]
    [MemberData(nameof(SecretPathSegmentRegexMarkers))]
    public void RedactPathSegments_prefilter_recognizes_every_regex_marker(string marker)
    {
        var result = AuditTextSanitizer.RedactPathSegments($"/v1/{marker}/abc123/status");

        Assert.Equal("/v1/[redacted]/[redacted]/status", result);
    }

    public static TheoryData<string> SecretPathSegmentRegexMarkers()
    {
        const string prefix = "(?i)(^|[-_.])(";
        const string suffix = ")([-_.=:]|$)";
        var pattern = SecretPathSegmentRegex().ToString();
        if (!pattern.StartsWith(prefix, StringComparison.Ordinal) ||
            !pattern.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SecretPathSegmentRegex marker pattern changed.");
        }

        var markers = pattern[prefix.Length..^suffix.Length].Split('|', StringSplitOptions.RemoveEmptyEntries);
        var data = new TheoryData<string>();
        foreach (var marker in markers)
        {
            data.Add(marker);
        }

        return data;
    }

    private static Regex SecretPathSegmentRegex()
    {
        var field = typeof(AuditTextSanitizer).GetField(
            "SecretPathSegmentRegex",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Regex)field.GetValue(null)!;
    }
}
