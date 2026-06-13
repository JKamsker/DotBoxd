using System.Security.Cryptography;
using System.Text.Json;
using SafeIR;
using SafeIR.Compiler;
using SafeIR.Compiler.Internal;
using SafeIR.Hosting;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CompiledCacheOriginTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public async Task Verifier_safe_cache_entry_with_stale_origin_proof_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        using var originKey = PersistentCompiledArtifactCacheOrigin.UseOriginKeyPathForCurrentAsyncFlow(
            Path.Combine(temp.Path, "compiled-cache-origin.key"));
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        var seed = await ExecuteCompiled(host, plan, input);
        Assert.True(seed.Succeeded, seed.Error?.SafeMessage);

        var entryPath = CacheEntry(temp.Path, plan);
        var forgedBytes = CompiledArtifactTestFactory.BuildI32Assembly(2, 2);
        var forgedHash = Convert.ToHexString(SHA256.HashData(forgedBytes)).ToLowerInvariant();
        var forgedManifest = await ReplaceManifestAsync(entryPath, manifest => manifest with { AssemblyHash = forgedHash });
        var forgedVerification = await new GeneratedAssemblyVerifier().VerifyAsync(
            forgedBytes,
            forgedManifest,
            VerificationPolicy.BoxedValueDefaults().WithExpectedManifest(VerificationManifestIdentity.FromManifest(forgedManifest)),
            CancellationToken.None);
        Assert.True(
            forgedVerification.Succeeded,
            string.Join("; ", forgedVerification.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        await File.WriteAllBytesAsync(Path.Combine(entryPath, "module.dll"), forgedBytes);
        await WriteVerificationAsync(entryPath, forgedVerification);

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
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
        var key = CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static async Task<ArtifactManifest> ReplaceManifestAsync(
        string entryPath,
        Func<ArtifactManifest, ArtifactManifest> replace)
    {
        var path = Path.Combine(entryPath, "manifest.json");
        ArtifactManifest manifest;
        await using (var read = File.OpenRead(path))
        {
            manifest = await JsonSerializer.DeserializeAsync<ArtifactManifest>(read, JsonOptions) ??
                throw new JsonException("empty manifest");
        }

        var replaced = replace(manifest);
        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, replaced, JsonOptions);
        return replaced;
    }

    private static async Task WriteVerificationAsync(string entryPath, VerificationResult verification)
    {
        var path = Path.Combine(entryPath, "verification.json");
        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, verification, JsonOptions);
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
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
