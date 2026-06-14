using System.Globalization;
using SafeIR.Compiler;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class Fix_ALG_0020_Tests
{
    [Fact]
    public async Task Persistent_cache_hit_does_not_re_run_generated_assembly_verifier()
    {
        using var temp = TempDirectory.Create();
        var (cache, plan, cacheKey) = await SeedVerifiedEntryAsync(temp.Path);
        var verifier = new CountingVerifier();

        var lookup = await cache.TryReadAsync(
            cacheKey,
            plan,
            "main",
            verifier,
            VerificationPolicy.BoxedValueDefaults(),
            CancellationToken.None);

        Assert.Equal(CompiledCacheStatus.Hit, lookup.Status);
        Assert.NotNull(lookup.Artifact);
        Assert.Equal(0, verifier.VerifyCalls);
    }

    [Fact]
    public async Task Persistent_cache_hit_reuses_the_cached_verification_record()
    {
        using var temp = TempDirectory.Create();
        var (cache, plan, cacheKey) = await SeedVerifiedEntryAsync(temp.Path);
        var verifier = new CountingVerifier();

        var lookup = await cache.TryReadAsync(
            cacheKey,
            plan,
            "main",
            verifier,
            VerificationPolicy.BoxedValueDefaults(),
            CancellationToken.None);

        Assert.Equal(CompiledCacheStatus.Hit, lookup.Status);
        var artifact = Assert.IsType<CompiledArtifact>(lookup.Artifact);
        Assert.True(artifact.Verification.Succeeded);
        Assert.Equal(artifact.AssemblyHash, artifact.Verification.AssemblyHash);
        Assert.Equal(artifact.Manifest.AssemblyHash, artifact.AssemblyHash);
    }

    [Fact]
    public async Task Tampered_cached_module_fails_closed_without_invoking_verifier()
    {
        using var temp = TempDirectory.Create();
        var (cache, plan, cacheKey) = await SeedVerifiedEntryAsync(temp.Path);
        var modulePath = Path.Combine(cache.EntryPath(cacheKey), "module.dll");
        var bytes = await File.ReadAllBytesAsync(modulePath);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(modulePath, bytes);
        var verifier = new CountingVerifier();

        var lookup = await cache.TryReadAsync(
            cacheKey,
            plan,
            "main",
            verifier,
            VerificationPolicy.BoxedValueDefaults(),
            CancellationToken.None);

        // The bytes-to-hash binding and the host-bound origin proof both reject the tampered
        // module, so the entry is quarantined and never reaches the read-path artifact, even
        // though the heavyweight verifier is no longer run on the steady-state hit path.
        Assert.Equal(CompiledCacheStatus.Invalid, lookup.Status);
        Assert.Null(lookup.Artifact);
        Assert.Equal(0, verifier.VerifyCalls);
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "quarantine")));
    }

    private static async Task<(PersistentCompiledArtifactCache Cache, ExecutionPlan Plan, string CacheKey)> SeedVerifiedEntryAsync(string root)
    {
        var host = SandboxTestHost.Create(compiler: true, compilerCache: root);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
        Assert.True(result.Succeeded, result.Error?.SafeMessage);

        var cache = new PersistentCompiledArtifactCache(root);
        var cacheKey = CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);
        Assert.True(cache.EntryExists(cacheKey));
        return (cache, plan, cacheKey);
    }

    private sealed class CountingVerifier : IGeneratedAssemblyVerifier
    {
        public int VerifyCalls { get; private set; }

        public ValueTask<VerificationResult> VerifyAsync(
            ReadOnlyMemory<byte> assemblyBytes,
            ArtifactManifest manifest,
            VerificationPolicy policy,
            CancellationToken cancellationToken)
        {
            VerifyCalls++;
            return new GeneratedAssemblyVerifier().VerifyAsync(assemblyBytes, manifest, policy, cancellationToken);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-alg0020-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
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
