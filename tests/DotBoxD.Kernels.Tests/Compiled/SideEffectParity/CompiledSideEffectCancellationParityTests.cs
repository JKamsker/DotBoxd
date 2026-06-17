using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Cancellation/timeout parity tests for side-effecting bindings.
/// Each test verifies that:
///   - Cancellation and timeout produce the expected Error.Code in both execution modes
///   - No partial side effect (sink delivery, log event) reaches the external observer
///   - The two modes are behaviorally identical for observable outputs
/// </summary>
public sealed class CompiledSideEffectCancellationParityTests
{
    // ---------------------------------------------------------------------------
    // 1. Pre-cancelled token: message sink binding stays empty
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_blocks_message_sink_delivery_in_interpreted_mode()
    {
        // Arrange
        var messages = new InMemoryPluginMessageSink();
        using var host = CancellationMessageHost(messages);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-send-interp"));
        var plan = await host.PrepareAsync(module, CancellationMessagePolicy());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        // Assert: Cancelled error, no message delivered
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Pre_cancelled_token_blocks_message_sink_delivery_in_compiled_mode_with_fallback()
    {
        // Arrange: compiled mode with AllowFallbackToInterpreter=true (current branch: side-effecting
        // bindings are not compiled directly yet; the request falls through to interpreter via fallback,
        // but the cancellation must still be honoured)
        var messages = new InMemoryPluginMessageSink();
        using var host = CancellationMessageHost(messages);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-send-compiled"));
        var plan = await host.PrepareAsync(module, CancellationMessagePolicy());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = true },
            cts.Token);

        // Assert: Cancelled error in whichever mode actually ran, no side effect
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    // ---------------------------------------------------------------------------
    // 2. Differential parity: pre-cancelled token on a PURE binding
    //    (pure bindings compile; this confirms the compiled kernel honours the
    //    outer CancellationToken and surfaces Cancelled — not a host failure)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_error_code_parity_on_pure_compiled_binding()
    {
        // Arrange: use a pure binding (compiles without fallback)
        using var host = CancellationPureHost();
        var module = await host.ImportJsonAsync(CancellationPureModuleJson("cancellation-pure-parity"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act: both interpreted and compiled run against the same pre-cancelled token
        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            cts.Token);

        // Assert: both modes fail with Cancelled (not HostFailure or ValidationError)
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, interpreted.Error!.Code);
        Assert.Equal(SandboxErrorCode.Cancelled, compiled.Error!.Code);

        // Actual mode labels confirm each path ran as requested
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    // ---------------------------------------------------------------------------
    // 3. Wall-time timeout: side-effecting (blocking) binding times out,
    //    no side effect delivered
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Wall_time_timeout_blocks_side_effecting_binding_delivery_no_partial_send()
    {
        // Arrange: blocking message binding that never returns unless cancelled;
        // wall-time budget of 30 ms forces a Timeout before the send completes
        var blockingMessages = new CancellationBlockingMessageSink();
        using var host = CancellationMessageHost(blockingMessages);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-timeout-send"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantHostMessageWrite()
                .WithFuel(10_000)
                .WithWallTime(TimeSpan.FromMilliseconds(30))
                .Build());

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Assert: Timeout error, no message committed to the sink
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.Empty(blockingMessages.CommittedMessages);
    }

    // ---------------------------------------------------------------------------
    // 4. Wall-time timeout parity on pure compiled binding
    //    (wall-time fires inside the compiled dispatch path)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Wall_time_timeout_parity_on_pure_compiled_binding()
    {
        // Arrange: pure slow binding that blocks until the wall-time fires
        using var host = CancellationSlowPureHost();
        var module = await host.ImportJsonAsync(CancellationSlowPureModuleJson("cancellation-wall-time-parity"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(10_000)
                .WithWallTime(TimeSpan.FromMilliseconds(50))
                .Build());

        // Act: both modes hit the wall-time deadline inside the binding
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

        // Assert: both modes surface Timeout (not HostFailure or BindingFailure)
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, interpreted.Error!.Code);
        Assert.Equal(SandboxErrorCode.Timeout, compiled.Error!.Code);
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    // ---------------------------------------------------------------------------
    // 5. Pre-cancelled token with log binding: log event not emitted on cancel
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_blocks_log_binding_event_in_interpreted_mode()
    {
        // Arrange: log.info is a SideEffectingExternal binding (Audit effect)
        using var host = CancellationLogHost();
        var module = await host.ImportJsonAsync(CancellationLogModuleJson("cancellation-log-pre-cancel"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(10_000)
                .Build());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        // Assert: Cancelled, no SandboxLog audit event emitted by the binding
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "SandboxLog");
    }

    // ---------------------------------------------------------------------------
    // 6. Message binding: pre-cancelled token and compiled-mode (with fallback)
    //    agree on error code — differential parity across both observable axes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pre_cancelled_token_error_code_matches_in_both_modes_for_message_binding()
    {
        // Arrange: run the same module interpreted vs compiled (with fallback)
        // A pre-cancelled token must produce Cancelled in both paths.
        const string moduleJson = """
        {
          "id": "cancellation-parity-message",
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
                    "args": [{ "string": "player-1" }, { "string": "ping" }]
                  }
                }
              ]
            }
          ]
        }
        """;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var messagesI = new InMemoryPluginMessageSink();
        var hostI = CancellationMessageHost(messagesI);
        var moduleI = await hostI.ImportJsonAsync(moduleJson);
        var planI = await hostI.PrepareAsync(moduleI, CancellationMessagePolicy());

        var messagesC = new InMemoryPluginMessageSink();
        var hostC = CancellationMessageHost(messagesC);
        var moduleC = await hostC.ImportJsonAsync(moduleJson);
        var planC = await hostC.PrepareAsync(moduleC, CancellationMessagePolicy());

        // Act
        var interpreted = await hostI.ExecuteAsync(
            planI, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);

        var compiled = await hostC.ExecuteAsync(
            planC, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = true },
            cts.Token);

        // Assert: both fail with same error code
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(interpreted.Error!.Code, compiled.Error!.Code);
        Assert.Equal(SandboxErrorCode.Cancelled, interpreted.Error.Code);

        // Neither sink received a message
        Assert.Empty(messagesI.Messages);
        Assert.Empty(messagesC.Messages);
    }

    // ---------------------------------------------------------------------------
    // 7. Cancellation mid-run (not pre-cancelled) via CancellationTokenSource
    //    for interpreted mode with a side-effecting blocking sink
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Mid_run_cancellation_blocks_blocking_sink_delivery_in_interpreted_mode()
    {
        // Arrange: the sink blocks (awaiting a TCS) so cancellation fires while the binding is pending
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var blockingSink = new CancellationTcsMessageSink(tcs.Task, cts.Token);

        using var host = CancellationMessageHost(blockingSink);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-mid-run-interp"));
        var plan = await host.PrepareAsync(module, CancellationMessagePolicy());

        // Cancel after a short delay to simulate mid-run cancellation
        var cancelTask = Task.Run(async () =>
        {
            await Task.Delay(50);
            await cts.CancelAsync();
        });

        // Act
        var result = await host.ExecuteAsync(
            plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);
        await cancelTask;

        // Assert: Cancelled error, no committed message
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.False(blockingSink.WasCommitted);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static SandboxHost CancellationMessageHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxHost CancellationLogHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxHost CancellationPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CancellationInstantPureBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxHost CancellationSlowPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CancellationSlowPureBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy CancellationMessagePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();

    private static string CancellationSendModuleJson(string id)
        => $$"""
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
                    "args": [{ "string": "player-1" }, { "string": "ping" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string CancellationLogModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write" }],
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
                    "call": "log.info",
                    "args": [{ "string": "should-not-emit" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string CancellationPureModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "test.cancellation.instant", "args": [] }
                }
              ]
            }
          ]
        }
        """;

    private static string CancellationSlowPureModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "test.cancellation.slow", "args": [] }
                }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// A pure binding that returns instantly. Used to exercise pre-cancelled-token
    /// detection before the binding itself does any work.
    /// </summary>
    private static BindingDescriptor CancellationInstantPureBinding()
        => new(
            "test.cancellation.instant",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(42)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    /// <summary>
    /// A pure binding that blocks until the wall-time cancellation token fires,
    /// simulating a long-running computation that triggers the wall-time budget.
    /// </summary>
    private static BindingDescriptor CancellationSlowPureBinding()
        => new(
            "test.cancellation.slow",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                return SandboxValue.FromInt32(0);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)))
        { IsAsync = true };

    // ---------------------------------------------------------------------------
    // Nested helpers (prefixed with "Cancellation" to avoid collisions when test
    // files are combined)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A message sink that blocks inside <c>SendAsync</c> until an external
    /// <see cref="TaskCompletionSource"/> releases it, and then records delivery
    /// only after the gate opens (never when cancelled before the gate fires).
    /// </summary>
    private sealed class CancellationBlockingMessageSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _committed = [];

        public IReadOnlyList<PluginMessage> CommittedMessages => _committed.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            // Block for long enough that the wall-time timeout fires first.
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            // Only reached if NOT cancelled — represents a committed side effect.
            _committed.Add(new PluginMessage(targetId, message));
        }
    }

    /// <summary>
    /// A message sink that waits on a <see cref="Task"/> gate, then commits only
    /// when the gate opens AND the run token has not yet fired.
    /// </summary>
    private sealed class CancellationTcsMessageSink(Task gate, CancellationToken runToken) : IPluginMessageSink
    {
        private bool _committed;

        public bool WasCommitted => _committed;

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            // Wait for the gate (or for either cancel token to fire).
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            runToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            _committed = true;
        }
    }
}
