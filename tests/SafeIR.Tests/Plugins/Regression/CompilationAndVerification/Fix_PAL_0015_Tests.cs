using SafeIR;
using SafeIR.Compiler;
using SafeIR.Hosting;
using SafeIR.Verifier;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0015: the persistent compiled artifact cache quarantines invalid
/// cache entries into a <c>quarantine</c> directory but never prunes them, so a long-lived host or
/// CI cache that repeatedly sees corrupted/stale compiled artifacts grows the quarantine tree
/// without bound.
///
/// This test drives the public host compiled execution path repeatedly: each iteration corrupts the
/// freshly recompiled cache entry and re-executes, which routes the invalid entry to quarantine and
/// recompiles. Every quarantine target is uniquely named (timestamp + GUID), so with no retention
/// policy the quarantine directory accumulates one retained payload per corruption forever.
///
/// The assertion encodes the CORRECT bounded behavior, so it is red until a bounded quarantine
/// cleanup policy lands. It is expressed entirely against existing public APIs.
/// </summary>
public sealed class Fix_PAL_0015_Tests
{
    // Number of corrupt-then-read cycles. Well above any reasonable quarantine retention bound.
    private const int CorruptionCycles = 40;

    // A correct bounded policy must retain far fewer than one quarantined payload per corruption.
    // We allow generous headroom for diagnostics while still proving growth is bounded.
    private const int MaxBoundedQuarantineEntries = 16;

    [Fact]
    public async Task Repeated_quarantine_does_not_grow_quarantine_directory_without_bound()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        // Seed the cache with a valid entry.
        var seed = await ExecuteCompiled(host, plan, input);
        Assert.True(seed.Succeeded);

        var entryPath = CacheEntry(temp.Path, plan);
        var quarantineRoot = Path.Combine(temp.Path, "quarantine");

        for (var i = 0; i < CorruptionCycles; i++)
        {
            // Corrupt the freshly (re)compiled cached module so the next read fails closed and
            // routes the entry to quarantine + recompile.
            await File.WriteAllBytesAsync(Path.Combine(entryPath, "module.dll"), [1, 2, 3, 4]);

            var result = await ExecuteCompiled(host, plan, input);
            Assert.True(result.Succeeded);
            Assert.Contains(
                result.AuditEvents,
                e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        }

        Assert.True(Directory.Exists(quarantineRoot));
        var quarantinedEntries = Directory.GetDirectories(quarantineRoot).Length;

        // Correct behavior: a bounded retention policy keeps the quarantine tree small regardless of
        // how many invalid entries have been seen over the cache lifetime. With the current
        // unbounded quarantine every corruption leaves a retained payload behind, so this count
        // equals CorruptionCycles and the assertion is red until pruning lands.
        Assert.True(
            quarantinedEntries <= MaxBoundedQuarantineEntries,
            $"expected the quarantine directory to be bounded by a retention policy " +
            $"(<= {MaxBoundedQuarantineEntries} entries), but it retained {quarantinedEntries} " +
            $"payloads after {CorruptionCycles} corruption cycles.");
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiled(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static string CacheEntry(string root, ExecutionPlan plan)
    {
        var key = CacheKey(plan);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static string CacheKey(ExecutionPlan plan)
        => CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-pal0015-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
