namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0024: the module/policy overload of
/// <see cref="BindingAuditFields.Create(string, System.DateTimeOffset, string, string, bool, long?, long?)"/>
/// builds the final dictionary once instead of copying the base overload's result via LINQ.
/// These tests pin the observable output so the allocation fix cannot silently change behavior.
/// </summary>
public sealed class Fix_PAL_0024_Tests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Module_policy_overload_includes_base_fields_and_hashes()
    {
        var fields = BindingAuditFields.Create(
            "network",
            StartedAt,
            moduleHash: "module-abc",
            policyHash: "policy-def",
            deterministic: true);

        Assert.Equal("network", fields["resourceKind"]);
        Assert.Equal("0.000", fields["durationMs"]);
        Assert.Equal("module-abc", fields["moduleHash"]);
        Assert.Equal("policy-def", fields["policyHash"]);
        Assert.Equal(4, fields.Count);
    }

    [Fact]
    public void Module_policy_overload_preserves_optional_byte_fields()
    {
        var fields = BindingAuditFields.Create(
            "file",
            StartedAt,
            moduleHash: "m",
            policyHash: "p",
            deterministic: true,
            bytesRead: 42,
            bytesWritten: 7);

        Assert.Equal("42", fields["bytesRead"]);
        Assert.Equal("7", fields["bytesWritten"]);
        Assert.Equal("m", fields["moduleHash"]);
        Assert.Equal("p", fields["policyHash"]);
        Assert.Equal(6, fields.Count);
    }

    [Fact]
    public void Module_policy_overload_omits_absent_byte_fields()
    {
        var fields = BindingAuditFields.Create(
            "log",
            StartedAt,
            moduleHash: "m",
            policyHash: "p",
            deterministic: true);

        Assert.False(fields.ContainsKey("bytesRead"));
        Assert.False(fields.ContainsKey("bytesWritten"));
        Assert.Equal(4, fields.Count);
    }

    [Fact]
    public void Module_policy_overload_matches_base_overload_on_shared_fields()
    {
        var baseFields = BindingAuditFields.Create(
            "network",
            StartedAt,
            deterministic: true,
            bytesRead: 100);

        var withHashes = BindingAuditFields.Create(
            "network",
            StartedAt,
            moduleHash: "module-abc",
            policyHash: "policy-def",
            deterministic: true,
            bytesRead: 100);

        foreach (var pair in baseFields)
        {
            Assert.True(withHashes.TryGetValue(pair.Key, out var value));
            Assert.Equal(pair.Value, value);
        }

        Assert.Equal(baseFields.Count + 2, withHashes.Count);
    }

    [Fact]
    public void Module_policy_overload_uses_ordinal_string_comparison()
    {
        var fields = BindingAuditFields.Create(
            "network",
            StartedAt,
            moduleHash: "m",
            policyHash: "p",
            deterministic: true);

        // Ordinal comparer is case-sensitive: a differently-cased key must not resolve.
        Assert.True(fields.ContainsKey("resourceKind"));
        Assert.False(fields.ContainsKey("RESOURCEKIND"));
    }
}
