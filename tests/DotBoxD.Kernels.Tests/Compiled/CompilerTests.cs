using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled;

public sealed class CompilerTests
{
    [Fact]
    public async Task Compiled_pure_module_matches_interpreter_result()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(7), SandboxValue.FromInt32(3)]);

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.False(string.IsNullOrWhiteSpace(compiled.ArtifactHash));
    }

    [Fact]
    public async Task Auto_mode_falls_back_to_interpreter_for_effectful_modules()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.json"), "from-file");
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.json"));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileRead(temp.Path, 1024)
            .WithFuel(5_000)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal("from-file", ((StringValue)result.Value!).Value);
    }

    [Fact]
    public async Task Compiled_mode_ignores_unreachable_effectful_tail_after_return()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(DeadFileReadTailJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(5_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)result.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(plan.FunctionAnalysis["main"].Effects.HasFlag(SandboxEffect.FileRead));
    }

    [Fact]
    public async Task Compiled_module_supports_internal_function_calls()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(InternalCallJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(7), SandboxValue.FromInt32(3)]);

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(145, ((I32Value)compiled.Value).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    [Fact]
    public async Task Compiled_internal_calls_enforce_call_depth_limit()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(CallDepthJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxCallDepth(2).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Reflection_emit_compiler_returns_unmaterialized_loaded_artifact()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());

        var artifact = await compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None);

        Assert.Equal(CompiledRuntimeFormKind.LoadedAssembly, artifact.RuntimeForm);
        Assert.NotEmpty(artifact.AssemblyBytes);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            _ = artifact.Entrypoint(
                    new SandboxContext(
                        SandboxRunId.New(),
                        plan.Policy,
                        new ResourceMeter(plan.Budget),
                        plan.Bindings,
                        new InMemoryAuditSink(),
                        CancellationToken.None),
                    SandboxValue.Unit);
        }));
    }

    [Fact]
    public void Call_depth_limit_is_part_of_policy_hash()
    {
        var first = SandboxPolicyBuilder.Create().WithMaxCallDepth(1).Build();
        var second = SandboxPolicyBuilder.Create().WithMaxCallDepth(2).Build();

        Assert.NotEqual(first.Hash, second.Hash);
    }

    private static string InternalCallJson()
        => """
        {
          "id": "compiled-internal-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "score",
              "parameters": [
                { "name": "level", "type": "I32" },
                { "name": "rarity", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } },
                    "right": { "op": "mul", "left": { "var": "rarity" }, "right": { "i32": 25 } }
                  }
                }
              ]
            },
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
                  "op": "return",
                  "value": { "call": "score", "args": [{ "var": "level" }, { "var": "rarity" }] }
                }
              ]
            }
          ]
        }
        """;

    private static string DeadFileReadTailJson()
        => """
        {
          "id": "dead-file-read-tail",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "i32": 1 } },
                {
                  "op": "expr",
                  "value": {
                    "call": "file.readText",
                    "args": [{ "path": "secret.txt" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string CallDepthJson()
        => """
        {
          "id": "compiled-call-depth",
          "version": "1.0.0",
          "functions": [
            {
              "id": "second",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            },
            {
              "id": "first",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "second", "args": [] } }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "first", "args": [] } }]
            }
          ]
        }
        """;

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-" + Guid.NewGuid().ToString("N"));
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
