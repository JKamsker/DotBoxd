using System.Reflection;
using System.Text.RegularExpressions;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0020: the shipped <see cref="SandboxPolicyBuilder.GrantFileWrite"/>
/// grant helper exposes <c>allowCreate</c> and <c>allowOverwrite</c> flags that decide whether a
/// granted write may create a missing target or replace an existing file. Those flags are part of
/// the safe host contract, but the public C# API spec page documents only the two-argument
/// <c>GrantFileWrite(string root, long maxBytesPerRun)</c> form. A host that follows the published
/// API page grants <c>file.write</c> and still gets denied writes because both modes default to
/// <c>false</c>, and has no documented way to discover the create/overwrite controls without reading
/// source or tests.
///
/// These tests first pin the real shipped builder surface (so the doc expectation is grounded in the
/// current public API, not invented), then require the public API spec to document the create and
/// overwrite policy controls, their <c>false</c> defaults, and at least one explicit create/overwrite
/// grant example. They are red until the public API docs are brought back into coherence with the
/// shipped grant surface.
/// </summary>
public sealed class Fix_API_0020_Tests
{
    private const string PublicApiSpecRelativePath =
        "docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md";

    [Fact]
    public void Shipped_GrantFileWrite_actually_exposes_create_and_overwrite_flags()
    {
        // Grounds the documentation expectation: the public builder really does carry these flags,
        // so omitting them from the spec is a genuine doc/code gap, not a stale finding.
        var method = typeof(SandboxPolicyBuilder).GetMethod(
            nameof(SandboxPolicyBuilder.GrantFileWrite),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);

        var parameters = method!.GetParameters().Select(p => p.Name).ToArray();
        Assert.Contains("allowCreate", parameters);
        Assert.Contains("allowOverwrite", parameters);
    }

    [Fact]
    public void Public_api_spec_documents_GrantFileWrite_create_and_overwrite_parameters()
    {
        var spec = ReadRepositoryText(PublicApiSpecRelativePath);

        // The published GrantFileWrite signature/description must name the policy-shaping flags that
        // every shipped overload requires hosts to reason about. Today the spec shows only the two
        // argument form, so these assertions fail until the create/overwrite controls are documented.
        Assert.Contains("allowCreate", spec, StringComparison.Ordinal);
        Assert.Contains("allowOverwrite", spec, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_api_spec_documents_safe_defaults_for_GrantFileWrite_modes()
    {
        var spec = ReadRepositoryText(PublicApiSpecRelativePath);

        // Operators reviewing file.write grants need to know both modes default to denied, so the
        // documented signature or prose must state the false defaults rather than leaving consumers
        // to infer the safe-by-default behavior from source or tests.
        Assert.Matches(
            new Regex(@"allowCreate[^\r\n]*false", RegexOptions.IgnoreCase),
            spec);
        Assert.Matches(
            new Regex(@"allowOverwrite[^\r\n]*false", RegexOptions.IgnoreCase),
            spec);
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(RepositoryRoot(), normalized);
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
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
