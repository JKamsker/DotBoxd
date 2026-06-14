using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

/// <summary>
/// CMP-0015 regression: proves the documented, user-facing compiled-cache happy path end to end
/// through the public package surface only (no internal test helpers). A host consumer configures
/// <see cref="SandboxHostBuilder.UseCompilerCache(string)"/> against a fresh cache root, imports a
/// minimal JSON module, prepares a plan, and runs compiled execution twice. The first run is a
/// recompiled miss; the second is a verified cache hit. Cache status is observed exactly the way a
/// consumer observes it: via the <c>RunSummary</c> audit event (forwarded live and on the result).
/// This locks the supported cache setup so future changes that silently break it fail here.
/// </summary>
public sealed class Fix_CMP_0015_Tests
{
    // Minimal entrypoint module a consumer might run; pure arithmetic keeps the example
    // deterministic and free of capability/effect requirements.
    private const string ScoreModuleJson = """
        {
          "id": "compiled-cache-smoke",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "capabilityRequests": [],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "level", "type": "I32" },
                { "name": "rarity", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "base",
                  "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
                },
                {
                  "op": "set",
                  "name": "bonus",
                  "value": { "op": "mul", "left": { "var": "rarity" }, "right": { "i32": 25 } }
                },
                {
                  "op": "return",
                  "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } }
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task UseCompilerCache_FirstRunIsMiss_SecondRunIsVerifiedHit()
    {
        using var cacheRoot = TempCacheRoot.CreateEmpty();

        // A consumer wires the cache exactly as the public API docs/sample show: configure the
        // cache directory, then enable the compiler. The forwarded audit observer is the supported
        // way to watch cache telemetry (cacheStatus) without reaching into internals.
        var auditLog = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerCache(cacheRoot.Path);
            builder.UseCompilerIfAvailable();
            builder.ForwardAuditEventsTo(auditLog.Add);
        });

        var module = await host.ImportJsonAsync(ScoreModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        var compiledOptions = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Compiled,
            AllowFallbackToInterpreter = false
        };

        // First run: cold cache, so the artifact is compiled, verified, and written. The consumer
        // sees a Miss in the run summary.
        var first = await host.ExecuteAsync(plan, "main", input, compiledOptions);

        // Second run: warm cache. The persisted artifact is re-verified before reuse and surfaces
        // as a Hit. Both runs must produce identical, successful compiled results.
        var second = await host.ExecuteAsync(plan, "main", input, compiledOptions);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(ExecutionMode.Compiled, first.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, second.ActualMode);
        Assert.Equal(SandboxValue.FromInt32(45), first.Value);
        Assert.Equal(SandboxValue.FromInt32(45), second.Value);

        // Cache telemetry is observable on the execution result (what a host inspects) ...
        Assert.Contains(
            first.AuditEvents,
            e => e.Message?.Contains("cacheStatus=Miss", StringComparison.Ordinal) == true);
        Assert.Contains(
            second.AuditEvents,
            e => e.Message?.Contains("cacheStatus=Hit", StringComparison.Ordinal) == true);

        // ... and on the forwarded live audit stream, in order: Miss then Hit.
        var observedStatuses = auditLog
            .Where(e => e.Message?.Contains("cacheStatus=", StringComparison.Ordinal) == true)
            .Select(e =>
                e.Message!.Contains("cacheStatus=Miss", StringComparison.Ordinal) ? "Miss" :
                e.Message!.Contains("cacheStatus=Hit", StringComparison.Ordinal) ? "Hit" :
                "Other")
            .ToList();

        Assert.Equal(new[] { "Miss", "Hit" }, observedStatuses);
    }

    private sealed class TempCacheRoot : IDisposable
    {
        private TempCacheRoot(string path) => Path = path;

        public string Path { get; }

        public static TempCacheRoot CreateEmpty()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-cmp0015-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempCacheRoot(path);
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
