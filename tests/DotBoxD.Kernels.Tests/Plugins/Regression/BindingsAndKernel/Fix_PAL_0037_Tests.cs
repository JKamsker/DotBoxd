using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

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
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .Build());

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
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .Build());

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

    [Fact]
    public async Task Compiled_async_sink_completes_under_blocked_single_threaded_context()
    {
        var interpretedSink = new YieldingMessageSink();
        var compiledSink = new YieldingMessageSink();
        var interpretedHost = MessageHost(interpretedSink, useCompiler: false);
        var compiledHost = MessageHost(compiledSink, useCompiler: true);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();
        var interpretedPlan = await interpretedHost.PrepareAsync(
            await interpretedHost.ImportJsonAsync(MessageSendJson("pal-0037-sync-context-i")),
            policy);
        var compiledPlan = await compiledHost.PrepareAsync(
            await compiledHost.ImportJsonAsync(MessageSendJson("pal-0037-sync-context-c")),
            policy);

        var interpreted = await interpretedHost.ExecuteAsync(
            interpretedPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var warmup = await compiledHost.ExecuteAsync(
            compiledPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
        Assert.True(warmup.Succeeded, warmup.Error?.SafeMessage);
        compiledSink.Clear();

        var pending = ExecuteWithBlockedSynchronizationContextAsync(() =>
            compiledHost.ExecuteAsync(
                compiledPlan,
                "main",
                SandboxValue.Unit,
                new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false }));
        var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(pending, completed);
        var compiled = await pending;
        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(interpretedSink.Messages.Single(), compiledSink.Messages.Single());
        Assert.Equal(
            interpreted.AuditEvents.Single(e => e.Kind == "PluginMessage").Message,
            compiled.AuditEvents.Single(e => e.Kind == "PluginMessage").Message);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
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

    private static SandboxHost MessageHost(YieldingMessageSink sink, bool useCompiler)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            if (useCompiler)
            {
                builder.UseCompilerIfAvailable();
            }
        });

    private static string MessageSendJson(string id) => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [{ "string": "player-1" }, { "string": "hello" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static Task<SandboxExecutionResult> ExecuteWithBlockedSynchronizationContextAsync(
        Func<ValueTask<SandboxExecutionResult>> execute)
    {
        var completion = new TaskCompletionSource<SandboxExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                completion.SetResult(execute().AsTask().GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        })
        {
            IsBackground = true,
            Name = "PAL-0037 blocked synchronization context"
        };
        thread.Start();
        return completion.Task;
    }

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
            })
            with
        {
            IsAsync = true
        };

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
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }

    private sealed class YieldingMessageSink : IPluginMessageSink
    {
        private readonly object _gate = new();
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages
        {
            get
            {
                lock (_gate)
                {
                    return [.. _messages];
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _messages.Clear();
            }
        }

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _messages.Add(new PluginMessage(targetId, message));
            }
        }
    }
}
