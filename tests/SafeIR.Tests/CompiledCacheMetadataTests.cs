using SafeIR.Compiler;
using SafeIR.Hosting;
using SafeIR.Verifier;
using System.Text.Json;

namespace SafeIR.Tests;

public sealed class CompiledCacheMetadataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        WriteIndented = true
    };

    [Fact]
    public async Task Cached_manifest_missing_optimization_flags_is_quarantined_and_recompiled()
        => await CorruptAndRecoverManifestAsync(m => m with { OptimizationFlags = null! });

    [Fact]
    public async Task Cached_manifest_missing_assembly_hash_is_quarantined_and_recompiled()
        => await CorruptAndRecoverManifestAsync(m => m with { AssemblyHash = "" });

    [Fact]
    public async Task Cached_verification_missing_assembly_hash_is_quarantined_and_recompiled()
        => await CorruptAndRecoverVerificationAsync(v => v with { AssemblyHash = "" });

    private static async Task CorruptAndRecoverManifestAsync(Func<ArtifactManifest, ArtifactManifest> replace)
    {
        using var temp = TempDirectory.Create();
        var (host, plan, input) = await PrepareCachedEntryAsync(temp.Path);
        await ReplaceManifestAsync(CacheEntry(temp.Path, plan), replace);

        var result = await ExecuteCompiled(host, plan, input);

        AssertRecovered(temp.Path, result);
    }

    private static async Task CorruptAndRecoverVerificationAsync(Func<VerificationResult, VerificationResult> replace)
    {
        using var temp = TempDirectory.Create();
        var (host, plan, input) = await PrepareCachedEntryAsync(temp.Path);
        await ReplaceVerificationAsync(CacheEntry(temp.Path, plan), replace);

        var result = await ExecuteCompiled(host, plan, input);

        AssertRecovered(temp.Path, result);
    }

    private static async Task<(SandboxHost Host, ExecutionPlan Plan, SandboxValue Input)> PrepareCachedEntryAsync(string cachePath)
    {
        var host = SandboxTestHost.Create(compiler: true, compilerCache: cachePath);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        return (host, plan, input);
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

    private static void AssertRecovered(string cachePath, SandboxExecutionResult result)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(cachePath, "quarantine")));
    }

    private static string CacheEntry(string root, ExecutionPlan plan)
    {
        var key = CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static async Task ReplaceManifestAsync(
        string entryPath,
        Func<ArtifactManifest, ArtifactManifest> replace)
    {
        var path = Path.Combine(entryPath, "manifest.json");
        ArtifactManifest manifest;
        await using (var read = File.OpenRead(path)) {
            manifest = await JsonSerializer.DeserializeAsync<ArtifactManifest>(read, JsonOptions) ??
                throw new JsonException("empty manifest");
        }

        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, replace(manifest), JsonOptions);
    }

    private static async Task ReplaceVerificationAsync(
        string entryPath,
        Func<VerificationResult, VerificationResult> replace)
    {
        var path = Path.Combine(entryPath, "verification.json");
        VerificationResult verification;
        await using (var read = File.OpenRead(path)) {
            verification = await JsonSerializer.DeserializeAsync<VerificationResult>(read, JsonOptions) ??
                throw new JsonException("empty verification");
        }

        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, replace(verification), JsonOptions);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
