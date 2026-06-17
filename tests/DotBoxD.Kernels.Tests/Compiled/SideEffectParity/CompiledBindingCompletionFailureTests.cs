using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledBindingCompletionFailureTests
{
    public static TheoryData<ExecutionMode, string> CompletedFailures()
        => new()
        {
            { ExecutionMode.Interpreted, "faulted" },
            { ExecutionMode.Compiled, "faulted" },
            { ExecutionMode.Interpreted, "canceled" },
            { ExecutionMode.Compiled, "canceled" }
        };

    [Theory]
    [MemberData(nameof(CompletedFailures))]
    public async Task Sync_declared_completed_binding_failure_is_observed_before_async_gate(
        ExecutionMode mode,
        string completion)
    {
        using var host = CreateHost(CompletedFailureBinding(completion));
        var module = await host.ImportJsonAsync(CallModuleJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Contains("binding 'test.completedFailure' failed", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("pending result", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_declared_pending_binding_succeeds_with_runtime_async_in_both_modes()
    {
        using var host = CreateHost(PendingSuccessBinding());
        var module = await host.ImportJsonAsync(CallModuleJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().AllowRuntimeAsync().WithFuel(1_000).Build());

        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
        {
            var result = await host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(42, ((I32Value)result.Value!).Value);
            Assert.Equal(mode, result.ActualMode);
        }
    }

    private static SandboxHost CreateHost(BindingDescriptor binding)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(binding);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static BindingDescriptor CompletedFailureBinding(string completion)
        => BaseBinding((_, _, _) => completion == "canceled"
            ? new ValueTask<SandboxValue>(Task.FromCanceled<SandboxValue>(new CancellationToken(canceled: true)))
            : new ValueTask<SandboxValue>(Task.FromException<SandboxValue>(new InvalidOperationException("secret"))));

    private static BindingDescriptor PendingSuccessBinding()
        => BaseBinding(async (_, _, _) =>
        {
            await Task.Yield();
            return SandboxValue.FromInt32(42);
        });

    private static BindingDescriptor BaseBinding(BindingInvoker invoke)
        => new(
            "test.completedFailure",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static string CallModuleJson()
        => """
        {
          "id": "completed-binding-failure",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "test.completedFailure", "args": [] } }
              ]
            }
          ]
        }
        """;
}
