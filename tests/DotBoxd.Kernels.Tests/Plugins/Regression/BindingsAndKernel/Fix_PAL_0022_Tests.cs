using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression tests for PAL-0022. File capability grant parameters
/// (<c>maxBytesPerRun</c>, <c>allowCreate</c>, <c>allowOverwrite</c>,
/// <c>allowedExtensions</c>) used to be reparsed from raw strings on every
/// <c>file.readText</c> / <c>file.writeText</c> binding call, including a fresh
/// CSV split of the extension list per call. They are now decoded once per
/// <see cref="CapabilityGrant"/> instance and cached by
/// <see cref="SafeFileGrantReader"/>, so repeated reads/writes under one stable
/// grant reuse the typed options instead of reallocating. These tests pin both the
/// decode-once behavior and the value semantics the bindings depend on.
/// </summary>
public sealed class Fix_PAL_0022_Tests
{
    private static CapabilityGrant Grant(params (string Key, string Value)[] parameters)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            map[key] = value;
        }

        return new CapabilityGrant("file.read", map);
    }

    [Fact]
    public void Read_returns_the_same_cached_options_for_the_same_grant()
    {
        var grant = Grant(
            ("root", "/root"),
            ("maxBytesPerRun", "1024"),
            ("allowedExtensions", ".json,.txt"));

        var first = SafeFileGrantReader.Read(grant);
        var second = SafeFileGrantReader.Read(grant);

        // Same instance proves parameters are decoded once and reused, not reparsed
        // (and the extension set is not re-split) on every binding call.
        Assert.Same(first, second);
        Assert.Same(first.AllowedExtensions, second.AllowedExtensions);
    }

    [Fact]
    public void Read_decodes_scalar_and_flag_parameters()
    {
        var grant = new CapabilityGrant(
            "file.write",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["root"] = "/root",
                ["maxBytesPerRun"] = "2048",
                ["allowCreate"] = "true",
                ["allowOverwrite"] = "false",
            });

        var options = SafeFileGrantReader.Read(grant);

        Assert.Equal(2048L, options.MaxBytesPerRun);
        Assert.True(options.AllowCreate);
        Assert.False(options.AllowOverwrite);
    }

    [Fact]
    public void Read_applies_defaults_when_optional_parameters_are_absent()
    {
        var options = SafeFileGrantReader.Read(Grant(("root", "/root")));

        Assert.Null(options.MaxBytesPerRun);
        Assert.False(options.AllowCreate);
        Assert.False(options.AllowOverwrite);
        Assert.Null(options.AllowedExtensions);
    }

    [Fact]
    public void Read_treats_blank_extension_list_as_no_restriction()
    {
        var options = SafeFileGrantReader.Read(Grant(("allowedExtensions", "   ")));

        Assert.Null(options.AllowedExtensions);
    }

    [Fact]
    public void Read_extension_set_matches_case_insensitively_like_the_previous_scan()
    {
        var options = SafeFileGrantReader.Read(Grant(("allowedExtensions", ".JSON, .Txt")));

        Assert.NotNull(options.AllowedExtensions);
        Assert.Contains(".json", options.AllowedExtensions!);
        Assert.Contains(".txt", options.AllowedExtensions!);
        Assert.DoesNotContain(".bin", options.AllowedExtensions!);
    }

    [Theory]
    [InlineData("maxBytesPerRun", "-1")]
    [InlineData("maxBytesPerRun", "not-a-number")]
    [InlineData("allowCreate", "maybe")]
    [InlineData("allowOverwrite", "1")]
    public void Read_fails_closed_on_invalid_parameters(string key, string value)
    {
        var grant = Grant(("root", "/root"), (key, value));

        var ex = Assert.Throws<SandboxRuntimeException>(() => SafeFileGrantReader.Read(grant));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
        Assert.Contains(key, ex.Error.SafeMessage, StringComparison.Ordinal);
    }
}
