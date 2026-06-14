using DotBoxd.Kernels;
using DotBoxd.Kernels.Compiler;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Kernels.Verifier;

namespace DotBoxd.Kernels.Tests;

public sealed class CompiledBindingCallTests
{
    [Fact]
    public async Task Compiled_mode_routes_host_binding_calls_through_runtime_stub()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(DoubleBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-binding-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.double", "args": [{ "i32": 21 }] } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(42, ((I32Value)compiled.Value!).Value);
        Assert.Equal(1, compiled.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Compiled_binding_stub_sanitizes_unexpected_binding_exceptions()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(ThrowingBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(CallBindingJson("test.throw"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        AssertBindingFailureIsSanitized(interpreted);
        AssertBindingFailureIsSanitized(compiled);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    [Fact]
    public async Task Compiled_runtime_rejects_binding_not_referenced_by_verified_plan()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(RogueBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new RogueBindingCompiler());
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-rogue-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Contains("not referenced", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiled_binding_stub_rejects_wrong_argument_shape_before_host_invoke()
    {
        var invoked = false;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(DoubleBinding(() => invoked = true));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new MalformedBindingArgumentCompiler());
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-binding-argument-shape",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.double", "args": [{ "i32": 21 }] } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.False(invoked);
    }

    private static BindingDescriptor DoubleBinding()
        => DoubleBinding(null);

    private static BindingDescriptor DoubleBinding(Action? invoked)
        => new(
            "test.double",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, args, _) =>
            {
                invoked?.Invoke();
                var value = ((I32Value)args[0]).Value;
                return ValueTask.FromResult(SandboxValue.FromInt32(value * 2));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor ThrowingBinding()
        => new(
            "test.throw",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => throw new InvalidOperationException("secret host detail"),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

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

    private static string CallBindingJson(string bindingId)
        => $$"""
        {
          "id": "compiled-binding-error",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "{{bindingId}}", "args": [] } }]
            }
          ]
        }
        """;

    private static void AssertBindingFailureIsSanitized(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.DoesNotContain("secret", result.Error.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

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

    private sealed class MalformedBindingArgumentCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildBindingCallAssembly(parameterCount: 0, "test.double")));
    }
}
