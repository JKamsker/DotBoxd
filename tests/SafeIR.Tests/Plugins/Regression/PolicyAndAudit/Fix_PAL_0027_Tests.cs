using SafeIR.Runtime;

namespace SafeIR.Tests;

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
}
