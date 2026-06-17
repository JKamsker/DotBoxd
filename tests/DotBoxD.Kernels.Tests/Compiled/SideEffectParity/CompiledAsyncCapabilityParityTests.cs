using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledAsyncCapabilityParityTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() { ExecutionMode.Interpreted, ExecutionMode.Compiled };

    [Fact]
    public async Task Async_binding_requires_runtime_async_at_prepare()
    {
        using var host = CreateHost(YieldingAsyncBinding());
        var module = await host.ImportJsonAsync(CallModuleJson("async-capability-denied"));
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-EFFECT");
    }

    [Fact]
    public async Task Wildcard_grant_authorizes_runtime_async_capability()
    {
        using var host = CreateHost(YieldingAsyncBinding());
        var module = await host.ImportJsonAsync(CallModuleJson("async-capability-wildcard"));
        var policy = SandboxPolicyBuilder.Create()
            .Grant("*", new { }, SandboxEffect.Concurrency)
            .WithFuel(1_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await ExecuteAsync(host, plan, ExecutionMode.Compiled);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(42, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Sync_only_binding_under_async_policy_runs_on_caller_thread()
    {
        using var host = CreateHost(ThreadIdBinding());
        var module = await host.ImportJsonAsync(CallModuleJson("sync-binding-no-worker"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromSeconds(2))
                .Build());
        var completion = new TaskCompletionSource<(int CallerThreadId, SandboxExecutionResult Result)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var callerThreadId = Environment.CurrentManagedThreadId;
            try
            {
                var result = host.ExecuteAsync(
                    plan,
                    "main",
                    SandboxValue.Unit,
                    new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false })
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                completion.SetResult((callerThreadId, result));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        var (callerThreadId, result) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(callerThreadId, ((I32Value)result.Value!).Value);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Sync_declared_pending_binding_fails_closed_at_runtime(ExecutionMode mode)
    {
        using var host = CreateHost(PendingSyncDeclaredBinding());
        var module = await host.ImportJsonAsync(CallModuleJson($"sync-pending-{mode}"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromSeconds(2))
                .Build());

        var result = await ExecuteAsync(host, plan, mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static SandboxHost CreateHost(BindingDescriptor binding)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(binding);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static BindingDescriptor YieldingAsyncBinding()
        => BaseBinding(async (_, _, _) =>
            {
                await Task.Yield();
                return SandboxValue.FromInt32(42);
            })
            with
            {
                IsAsync = true
            };

    private static BindingDescriptor ThreadIdBinding()
        => BaseBinding((_, _, _) =>
            ValueTask.FromResult(SandboxValue.FromInt32(Environment.CurrentManagedThreadId)));

    private static BindingDescriptor PendingSyncDeclaredBinding()
        => BaseBinding((_, _, _) =>
        {
            var pending = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            return new ValueTask<SandboxValue>(pending.Task);
        });

    private static BindingDescriptor BaseBinding(BindingInvoker invoke)
        => new(
            "test.asyncValue",
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

    private static string CallModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "test.asyncValue", "args": [] } }
              ]
            }
          ]
        }
        """;
}

public sealed class CompiledAsyncSynchronizationContextParityTests
{
    [Fact]
    public async Task Compiled_async_binding_completes_when_caller_sync_context_is_blocked()
    {
        using var host = CreateHost();
        var module = await host.ImportJsonAsync(CallModuleJson("async-sync-context"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromSeconds(2))
                .Build());
        var completion = new TaskCompletionSource<SandboxExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                var result = host.ExecuteAsync(
                    plan,
                    "main",
                    SandboxValue.Unit,
                    new SandboxExecutionOptions
                    {
                        Mode = ExecutionMode.Compiled,
                        AllowFallbackToInterpreter = false
                    }).AsTask().GetAwaiter().GetResult();
                completion.SetResult(result);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        var finished = await Task.WhenAny(
            completion.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(completion.Task, finished);
        var run = await completion.Task;
        Assert.True(run.Succeeded, run.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, run.ActualMode);
        Assert.Equal(42, ((I32Value)run.Value!).Value);
    }

    private static SandboxHost CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(YieldingAsyncBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static BindingDescriptor YieldingAsyncBinding()
        => new BindingDescriptor(
            "test.syncContextYield",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            async (_, _, _) =>
            {
                await Task.Yield();
                return SandboxValue.FromInt32(42);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private static string CallModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "test.syncContextYield", "args": [] } }
              ]
            }
          ]
        }
        """;

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
        }
    }
}
