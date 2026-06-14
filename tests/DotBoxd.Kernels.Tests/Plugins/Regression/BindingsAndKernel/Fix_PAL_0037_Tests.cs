using DotBoxd.Kernels;
using DotBoxd.Kernels.Compiler;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Kernels.Verifier;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0037: compiled binding dispatch must read the
/// synchronous <see cref="ValueTask{TResult}"/> fast path without forcing an
/// <c>AsTask()</c> wrapper allocation, while still correctly resolving bindings
/// that complete asynchronously.
/// </summary>
public sealed class Fix_PAL_0037_Tests
{
    [Fact]
    public async Task Compiled_dispatch_returns_value_for_synchronous_binding()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(SynchronousDoubleBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(CallDoubleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(42, ((I32Value)compiled.Value!).Value);
    }

    [Fact]
    public async Task Compiled_dispatch_resolves_asynchronously_completing_binding()
    {
        // A binding whose ValueTask is not completed synchronously must still
        // resolve correctly through the AsTask() fallback retained by the fix.
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(AsynchronousDoubleBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(CallDoubleJson);
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
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(42, ((I32Value)compiled.Value!).Value);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
    }

    private const string CallDoubleJson = """
    {
      "id": "pal-0037-binding-call",
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
    """;

    private static BindingDescriptor SynchronousDoubleBinding()
        => DoubleBinding((_, args, _) =>
        {
            var value = ((I32Value)args[0]).Value;
            return ValueTask.FromResult(SandboxValue.FromInt32(value * 2));
        });

    private static BindingDescriptor AsynchronousDoubleBinding()
        => DoubleBinding(async (_, args, _) =>
        {
            await Task.Yield();
            var value = ((I32Value)args[0]).Value;
            return SandboxValue.FromInt32(value * 2);
        });

    private static BindingDescriptor DoubleBinding(BindingInvoker invoke)
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
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
