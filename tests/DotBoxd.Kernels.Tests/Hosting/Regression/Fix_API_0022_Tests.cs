namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0022: the public API compatibility baseline gate
/// (<c>scripts/check-api-compat-baseline.ps1</c>) must measure <em>effective</em> public
/// accessibility. Members declared <c>public</c>/<c>protected</c> inside an
/// <c>internal</c>/<c>private</c>/<c>file</c> containing type are not reachable by a NuGet
/// consumer, so they must never appear in <c>docs/api-baselines/*.txt</c>. These tests pin
/// the regenerated baselines against the internal compiler/analyzer helpers cited in the
/// finding while proving the genuinely public package surface is preserved.
/// </summary>
public sealed class Fix_API_0022_Tests
{
    [Theory]
    // DotBoxd.Kernels.Compiler internal helpers cited in the finding.
    [InlineData("DotBoxd.Kernels.Compiler", "src/Kernels/DotBoxd.Kernels.Compiler/Emitters/MethodEmitter.cs")]
    [InlineData("DotBoxd.Kernels.Compiler", "src/Kernels/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs")]
    [InlineData("DotBoxd.Kernels.Compiler", "src/Kernels/DotBoxd.Kernels.Compiler/Internal/PersistentCompiledArtifactCacheValidator.cs")]
    // DotBoxd.Plugins.Analyzer internal helpers cited in the finding.
    [InlineData("DotBoxd.Plugins.Analyzer", "src/CodeGeneration/DotBoxd.Plugins.Analyzer/Analysis/EquatableArray.cs")]
    [InlineData("DotBoxd.Plugins.Analyzer", "src/CodeGeneration/DotBoxd.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxdExpressionLoweringContext.cs")]
    public void Baseline_excludes_public_members_of_internal_types(string packageId, string sourceRelativePath)
    {
        var sourcePath = Path.Combine(RepositoryRoot(), sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(sourcePath), $"Cited source file is missing: {sourcePath}");

        var sourceText = File.ReadAllText(sourcePath);

        // The cited files are top-level internal/private/file types. If that ever changes
        // this test would no longer prove the gate, so guard the precondition explicitly.
        Assert.True(
            DeclaresOnlyNonPublicTopLevelTypes(sourceText),
            $"Expected {sourceRelativePath} to declare only non-public top-level types.");

        var baseline = BaselineEntries(packageId);
        var memberNames = PublicMemberNames(sourceText);
        Assert.NotEmpty(memberNames);

        foreach (var member in memberNames)
        {
            Assert.DoesNotContain(
                baseline,
                entry => MentionsMember(entry, member));
        }
    }

    [Fact]
    public void Compiler_baseline_drops_specific_internal_helper_signatures()
    {
        var baseline = BaselineEntries("DotBoxd.Kernels.Compiler");

        // Exact signatures the old text-only extractor wrongly recorded (finding evidence).
        Assert.DoesNotContain(baseline, e => e.Contains("MethodEmitter(", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline, e => e.Contains("public void Emit()", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline, e => e.Contains("MethodInfo Runtime(string name)", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline, e => e.Contains("ValidateManifest(", StringComparison.Ordinal));
        // Members of the private nested EntryLock/EntryLockLease types must also be gone.
        Assert.DoesNotContain(baseline, e => e.Contains("EntryLockLease(", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline, e => e.Contains("SemaphoreSlim Semaphore", StringComparison.Ordinal));
    }

    [Fact]
    public void Compiler_baseline_still_records_real_public_surface()
    {
        var baseline = BaselineEntries("DotBoxd.Kernels.Compiler");

        Assert.Contains(baseline, e => e.Contains("interface ISandboxCompiler", StringComparison.Ordinal));
        Assert.Contains(baseline, e => e.Contains("class ReflectionEmitSandboxCompiler", StringComparison.Ordinal));
        Assert.Contains(baseline, e => e.Contains("record CompileOptions", StringComparison.Ordinal));
        Assert.Contains(baseline, e => e.Contains("enum CompiledCacheStatus", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginAnalyzer_baseline_drops_internal_helpers_but_keeps_public_analyzers()
    {
        var baseline = BaselineEntries("DotBoxd.Plugins.Analyzer");

        // Internal helpers (EquatableArray, its nested Enumerator, lowering context).
        Assert.DoesNotContain(baseline, e => e.Contains("EquatableArray(", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline, e => e.Contains("Enumerator GetEnumerator", StringComparison.Ordinal));
        Assert.DoesNotContain(baseline, e => e.Contains("DotBoxdExpressionLoweringContext(", StringComparison.Ordinal));

        // Genuinely public analyzer surface (must stay public for Roslyn discovery).
        Assert.Contains(baseline, e => e.Contains("class DotBoxdPluginAnalyzer", StringComparison.Ordinal));
        Assert.Contains(baseline, e => e.Contains("class DotBoxdPluginPackageGenerator", StringComparison.Ordinal));
    }

    private static bool MentionsMember(string baselineEntry, string memberName)
    {
        // Match the member name as a whole identifier to avoid coincidental substrings.
        var index = baselineEntry.IndexOf(memberName, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 ? ' ' : baselineEntry[index - 1];
            var afterIndex = index + memberName.Length;
            var after = afterIndex >= baselineEntry.Length ? ' ' : baselineEntry[afterIndex];
            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
            {
                return true;
            }

            index = baselineEntry.IndexOf(memberName, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsIdentifierChar(char value)
        => char.IsLetterOrDigit(value) || value == '_';

    private static bool DeclaresOnlyNonPublicTopLevelTypes(string sourceText)
    {
        // A top-level (non-indented) public/protected type would make members legitimately
        // public; the cited fixtures must not have one.
        foreach (var rawLine in sourceText.Split('\n'))
        {
            if (rawLine.Length == 0 || char.IsWhiteSpace(rawLine[0]))
            {
                continue;
            }

            var line = rawLine.TrimEnd();
            if ((line.StartsWith("public ", StringComparison.Ordinal) ||
                 line.StartsWith("protected ", StringComparison.Ordinal)) &&
                IsTypeDeclaration(line))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTypeDeclaration(string line)
        => line.Contains(" class ", StringComparison.Ordinal) ||
           line.Contains(" struct ", StringComparison.Ordinal) ||
           line.Contains(" interface ", StringComparison.Ordinal) ||
           line.Contains(" enum ", StringComparison.Ordinal) ||
           line.Contains(" record ", StringComparison.Ordinal);

    // Ubiquitous identifiers (object overrides, common collection members) can legitimately
    // appear on unrelated public types, so excluding them keeps the source-driven scan from
    // producing false positives while still covering helper-specific member names.
    private static readonly HashSet<string> CommonMemberNames = new(StringComparer.Ordinal)
    {
        "Equals",
        "GetHashCode",
        "ToString",
        "GetEnumerator",
        "Count",
        "Current",
        "MoveNext",
        "Dispose",
        "this",
    };

    private static IReadOnlyList<string> PublicMemberNames(string sourceText)
    {
        var names = new List<string>();
        foreach (var rawLine in sourceText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("public ", StringComparison.Ordinal) || IsTypeDeclaration(" " + line))
            {
                continue;
            }

            // Capture the declared member identifier: the token immediately before '('
            // for methods/constructors, or before '{'/'=>'/';' for properties/fields.
            var name = ExtractMemberName(line);
            if (!string.IsNullOrEmpty(name) && !CommonMemberNames.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string ExtractMemberName(string line)
    {
        var cut = line.Length;
        foreach (var terminator in new[] { '(', '{', ';', '=' })
        {
            var index = line.IndexOf(terminator);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        var head = line[..cut].TrimEnd();
        var lastSpace = head.LastIndexOf(' ');
        var token = lastSpace >= 0 ? head[(lastSpace + 1)..] : head;

        // Strip generic arity and array/nullable noise; keep a plain identifier only.
        var stripped = new string(token.TakeWhile(IsIdentifierChar).ToArray());
        return stripped;
    }

    private static IReadOnlyList<string> BaselineEntries(string packageId)
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "api-baselines", $"{packageId}.txt");
        Assert.True(File.Exists(path), $"Missing regenerated baseline: {path}");

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) &&
                           !line.TrimStart().StartsWith('#'))
            .ToArray();
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
