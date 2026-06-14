using SafeIR.Compiler;
using SafeIR.Hosting;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class BindingResolutionTests
{
    [Fact]
    public async Task Compiled_runtime_rejects_binding_referenced_only_by_dead_function()
    {
        var host = CreateHost(new RogueBindingCompiler());
        var module = await host.ImportJsonAsync(DeadFunctionBindingJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteCompiledAsync(host, plan);

        AssertNotReferenced(result);
    }

    [Fact]
    public async Task User_function_calls_shadow_same_named_bindings()
    {
        var host = CreateHost();
        var module = await host.ImportJsonAsync(ShadowedBindingJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await ExecuteCompiledAsync(host, plan);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(7, ((I32Value)compiled.Value!).Value);
        Assert.Equal(0, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(0, compiled.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Compiled_runtime_rejects_binding_shadowed_by_user_function()
    {
        var host = CreateHost(new RogueBindingCompiler());
        var module = await host.ImportJsonAsync(ShadowedBindingJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await ExecuteCompiledAsync(host, plan);

        AssertNotReferenced(result);
    }

    private static SandboxHost CreateHost(ISandboxCompiler? compiler = null)
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddBinding(RogueBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static void AssertNotReferenced(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Contains("not referenced", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    private static BindingDescriptor RogueBinding()
        => new(
            "test.rogue",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(999)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string DeadFunctionBindingJson()
        => """
        {
          "id": "compiled-dead-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            },
            {
              "id": "dead",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.rogue", "args": [] } }]
            }
          ]
        }
        """;

    private static string ShadowedBindingJson()
        => """
        {
          "id": "compiled-shadowed-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.rogue", "args": [] } }]
            },
            {
              "id": "test.rogue",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 7 } }]
            }
          ]
        }
        """;

    private sealed class RogueBindingCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildBindingCallAssembly(parameterCount: 0, "test.rogue")));
    }
}
