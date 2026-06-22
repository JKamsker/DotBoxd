using System.Text.Json;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;
namespace DotBoxD.Kernels.Tests.Compiled.Core;
public sealed class CompiledCacheTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    [Fact]
    public async Task Compiled_artifact_is_persisted_and_reused()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        var first = await ExecuteCompiled(host, plan, input);
        var second = await ExecuteCompiled(host, plan, input);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Contains(first.AuditEvents, e => e.Message?.Contains("cacheStatus=Miss", StringComparison.Ordinal) == true);
        Assert.Contains(second.AuditEvents, e => e.Message?.Contains("cacheStatus=Hit", StringComparison.Ordinal) == true);
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "module.dll")));
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "manifest.json")));
        Assert.True(File.Exists(Path.Combine(CacheEntry(temp.Path, plan), "verification.json")));
    }
    [Fact]
    public async Task Policy_hash_change_uses_a_different_cache_key()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var firstPlan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var secondPlan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(2_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, firstPlan, input);
        _ = await ExecuteCompiled(host, secondPlan, input);
        Assert.NotEqual(CacheKey(firstPlan), CacheKey(secondPlan));
        Assert.True(Directory.Exists(CacheEntry(temp.Path, firstPlan)));
        Assert.True(Directory.Exists(CacheEntry(temp.Path, secondPlan)));
    }

    [Fact]
    public async Task Binding_manifest_change_uses_a_different_cache_key()
    {
        using var temp = TempDirectory.Create();
        var defaultHost = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var extendedHost = HostWithExtraBinding(temp.Path);
        var module = await defaultHost.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var defaultPlan = await defaultHost.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var extendedPlan = await extendedHost.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        _ = await ExecuteCompiled(defaultHost, defaultPlan, input);
        _ = await ExecuteCompiled(extendedHost, extendedPlan, input);

        Assert.NotEqual(defaultPlan.BindingManifestHash, extendedPlan.BindingManifestHash);
        Assert.NotEqual(CacheKey(defaultPlan), CacheKey(extendedPlan));
        Assert.True(Directory.Exists(CacheEntry(temp.Path, defaultPlan)));
        Assert.True(Directory.Exists(CacheEntry(temp.Path, extendedPlan)));
    }

    [Fact]
    public async Task Corrupted_cached_dll_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await File.WriteAllBytesAsync(Path.Combine(CacheEntry(temp.Path, plan), "module.dll"), [1, 2, 3, 4]);

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "quarantine")));
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    [Fact]
    public async Task Corrupted_cached_manifest_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await File.WriteAllTextAsync(Path.Combine(CacheEntry(temp.Path, plan), "manifest.json"), "{ broken json");

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    [Fact]
    public async Task Cached_dll_manifest_hash_mismatch_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await ReplaceManifestAssemblyHashAsync(CacheEntry(temp.Path, plan), new string('0', 64));

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    [Fact]
    public async Task Cached_manifest_verifier_version_mismatch_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await ReplaceManifestAsync(CacheEntry(temp.Path, plan), m => m with { VerifierVersion = "stale-verifier" });

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    [Fact]
    public async Task Cached_manifest_optimization_flag_mismatch_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await ReplaceManifestAsync(CacheEntry(temp.Path, plan), m => m with { OptimizationFlags = ["opt"] });

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    [Fact]
    public async Task Corrupted_cached_verification_metadata_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await File.WriteAllTextAsync(Path.Combine(CacheEntry(temp.Path, plan), "verification.json"), "{ broken json");

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e => e.Message?.Contains("cacheStatus=Recompiled", StringComparison.Ordinal) == true);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(temp.Path, "quarantine")));
    }

    [Fact]
    public async Task Cached_verification_version_mismatch_is_quarantined_and_recompiled()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);
        _ = await ExecuteCompiled(host, plan, input);
        await ReplaceVerificationAsync(CacheEntry(temp.Path, plan), v => v with { VerifierVersion = "stale-verifier" });

        var result = await ExecuteCompiled(host, plan, input);

        Assert.True(result.Succeeded);
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
        var key = CacheKey(plan);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static string CacheKey(ExecutionPlan plan)
        => CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);

    private static SandboxHost HostWithExtraBinding(string cacheDirectory)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(ExtraBinding());
            builder.UseInterpreter();
            builder.UseCompilerCache(cacheDirectory);
            builder.UseCompilerIfAvailable();
        });

    private static BindingDescriptor ExtraBinding()
        => new(
            "test.extra",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static async Task ReplaceManifestAssemblyHashAsync(string entryPath, string assemblyHash)
        => await ReplaceManifestAsync(entryPath, manifest => manifest with { AssemblyHash = assemblyHash });

    private static async Task ReplaceManifestAsync(
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

        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, replace(manifest), JsonOptions);
    }

    private static async Task ReplaceVerificationAsync(
        string entryPath,
        Func<VerificationResult, VerificationResult> replace)
    {
        var path = Path.Combine(entryPath, "verification.json");
        VerificationResult verification;
        await using (var read = File.OpenRead(path))
        {
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-cache-" + Guid.NewGuid().ToString("N"));
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
