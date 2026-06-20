using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledCacheEntrypointTests
{
    [Fact]
    public async Task Compiled_cache_key_includes_entrypoint()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create(compiler: true, compilerCache: temp.Path);
        var module = await host.ImportJsonAsync("""
        {
          "id": "multi-entrypoint-cache",
          "version": "1.0.0",
          "functions": [
            {
              "id": "first",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            },
            {
              "id": "second",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 2 } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var first = await ExecuteCompiled(host, plan, "first");
        var second = await ExecuteCompiled(host, plan, "second");

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)first.Value!).Value);
        Assert.Equal(2, ((I32Value)second.Value!).Value);
        Assert.NotEqual(CacheKey(plan, "first"), CacheKey(plan, "second"));
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiled(
        SandboxHost host,
        ExecutionPlan plan,
        string entrypoint)
        => await host.ExecuteAsync(
            plan,
            entrypoint,
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static string CacheKey(ExecutionPlan plan, string entrypoint)
        => CacheKeyBuilder.Build(plan, entrypoint, VerificationPolicy.BoxedValueDefaults(), optimize: false);

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
