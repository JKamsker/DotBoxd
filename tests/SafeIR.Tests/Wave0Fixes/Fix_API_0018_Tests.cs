using System.Text.RegularExpressions;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class Fix_API_0018_Tests
{
    private const string ReferenceFileName = "VerifierDiagnosticCodes.cs";

    private static readonly Regex EmittedCodePattern = new(
        "\"(?<code>V-[A-Z0-9-]+)\"",
        RegexOptions.Compiled);

    [Fact]
    public void Public_reference_documents_every_emitted_verifier_code()
    {
        var emitted = EmittedVerifierCodes();
        var documented = VerifierDiagnosticCodes.All
            .Select(reference => reference.Code)
            .ToHashSet(StringComparer.Ordinal);

        var undocumented = emitted.Where(code => !documented.Contains(code)).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.True(
            undocumented.Length == 0,
            $"Verifier emits codes with no public reference entry: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void Public_reference_has_no_codes_that_the_verifier_never_emits()
    {
        var emitted = EmittedVerifierCodes();

        var orphaned = VerifierDiagnosticCodes.All
            .Select(reference => reference.Code)
            .Where(code => !emitted.Contains(code))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphaned.Length == 0,
            $"Public reference lists codes the verifier never emits: {string.Join(", ", orphaned)}");
    }

    [Fact]
    public void Public_reference_entries_are_well_formed_and_unique()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in VerifierDiagnosticCodes.All)
        {
            Assert.StartsWith("V-", reference.Code, StringComparison.Ordinal);
            Assert.True(codes.Add(reference.Code), $"Duplicate reference entry for {reference.Code}");
            Assert.False(string.IsNullOrWhiteSpace(reference.Meaning));
            Assert.False(string.IsNullOrWhiteSpace(reference.LikelyCause));
            Assert.False(string.IsNullOrWhiteSpace(reference.Remediation));
        }
    }

    [Fact]
    public void TryGetReference_round_trips_known_codes()
    {
        Assert.True(VerifierDiagnosticCodes.TryGetReference(VerifierDiagnosticCodes.ManifestHash, out var reference));
        Assert.Equal(VerifierDiagnosticCodes.ManifestHash, reference.Code);
        Assert.Equal(VerifierDiagnosticCategory.ArtifactIntegrity, reference.Category);
    }

    [Fact]
    public void TryGetReference_fails_closed_for_unknown_codes()
    {
        Assert.False(VerifierDiagnosticCodes.TryGetReference("V-NOT-A-REAL-CODE", out var reference));
        Assert.Equal("V-NOT-A-REAL-CODE", reference.Code);
        Assert.False(reference.ExpectedFromCompilerOutput);
        Assert.False(string.IsNullOrWhiteSpace(reference.Remediation));
    }

    private static HashSet<string> EmittedVerifierCodes()
    {
        var verifierRoot = Path.Combine(RepositoryRoot(), "src", "SafeIR.Verifier");
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(verifierRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.GetFileName(file).Equals(ReferenceFileName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in EmittedCodePattern.Matches(File.ReadAllText(file)))
            {
                codes.Add(match.Groups["code"].Value);
            }
        }

        Assert.NotEmpty(codes);
        return codes;
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
