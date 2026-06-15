using DotBoxD.Hosting;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Regression coverage for the Codex P2 finding: a binding argument may itself be a side-effecting
/// binding call (now compilable under #27). The compiled emitter must evaluate arguments BEFORE the
/// synthetic value-array fuel/allocation charge, exactly like the interpreter, so a tight budget can
/// never throw QuotaExceeded before a side-effecting argument runs in compiled mode while the
/// interpreter would have executed it.
///
/// host.message.send(app.touch(), "hello") puts a side-effecting binding call (app.touch, which
/// records each run) in the first argument of a two-argument binding. We sweep the allocation budget
/// and count budgets where the argument ran in only one mode. A naive per-budget equality is fragile
/// because compiled mode carries a small documented resource-accounting overhead, so a couple of
/// boundary budgets always differ; instead we assert the COUNT stays within a small tolerance. With
/// the fix the argument is evaluated first in both modes, so divergence is confined to that boundary
/// noise; without the fix the compiled run skips the argument across the whole value-array-charge
/// window, far exceeding the tolerance.
/// </summary>
public sealed class CompiledSideEffectArgumentOrderParityTests
{
    private const string ModuleJson = """
    {
      "id": "side-effecting-argument-order",
      "version": "1.0.0",
      "capabilityRequests": [ { "id": "host.message.write" }, { "id": "game.write" } ],
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
                "args": [
                  { "call": "app.touch", "args": [] },
                  { "string": "hello" }
                ]
              }
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public async Task Side_effecting_argument_is_not_skipped_by_the_value_array_charge()
    {
        const int tolerance = 12; // boundary-accounting noise; the bug skips the argument far more widely.
        var skippedByCompiled = 0;
        var divergent = 0;

        for (var maxAllocatedBytes = 1; maxAllocatedBytes <= 256; maxAllocatedBytes++)
        {
            var interpretedTouches = await RunAsync(ExecutionMode.Interpreted, maxAllocatedBytes);
            var compiledTouches = await RunAsync(ExecutionMode.Compiled, maxAllocatedBytes);
            if (interpretedTouches != compiledTouches)
            {
                divergent++;
                if (interpretedTouches > compiledTouches)
                {
                    skippedByCompiled++; // compiled skipped the argument the interpreter ran — the bug.
                }
            }
        }

        Assert.True(
            divergent <= tolerance,
            $"Side-effecting argument ran in only one mode at {divergent} budgets " +
            $"({skippedByCompiled} where compiled skipped it) — exceeds tolerance {tolerance}. " +
            "The compiled emitter is likely charging the value array before evaluating arguments.");
    }

    private static async Task<int> RunAsync(ExecutionMode mode, int maxAllocatedBytes)
    {
        var touches = 0;
        var sink = new InMemoryPluginMessageSink();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddBinding(TouchBinding(() => touches++));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

        var module = await host.ImportJsonAsync(ModuleJson);
        var policy = new SandboxPolicy(
            "touch-and-send",
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [
                new CapabilityGrant("host.message.write", new Dictionary<string, string>()),
                new CapabilityGrant("game.write", new Dictionary<string, string>())
            ],
            new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: maxAllocatedBytes));
        var plan = await host.PrepareAsync(module, policy);

        await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return touches;
    }

    // The same guarantee for the >2-argument array path (ValueArrayEmitter / CreateLiteralValueArray +
    // CallBinding), using a 6-argument call. Six arguments previously failed compiled verification
    // under a grouped-store ordering, so a passing run proves both (a) no meter-density regression —
    // the call still compiles — and (b) the side-effecting first argument is not skipped by the array
    // charge.
    private const string ArrayPathModuleJson = """
    {
      "id": "side-effecting-argument-order-array",
      "version": "1.0.0",
      "capabilityRequests": [ { "id": "game.write" } ],
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
                "call": "app.consumeMany",
                "args": [
                  { "call": "app.touch", "args": [] },
                  { "string": "b" }, { "string": "c" }, { "string": "d" }, { "string": "e" }, { "string": "f" }
                ]
              }
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public async Task Side_effecting_argument_in_array_path_call_is_not_skipped()
    {
        const int tolerance = 12;
        var skippedByCompiled = 0;
        var divergent = 0;

        for (var maxAllocatedBytes = 1; maxAllocatedBytes <= 256; maxAllocatedBytes++)
        {
            var interpretedTouches = await RunArrayPathAsync(ExecutionMode.Interpreted, maxAllocatedBytes);
            var compiledTouches = await RunArrayPathAsync(ExecutionMode.Compiled, maxAllocatedBytes);
            if (interpretedTouches != compiledTouches)
            {
                divergent++;
                if (interpretedTouches > compiledTouches)
                {
                    skippedByCompiled++;
                }
            }
        }

        Assert.True(
            divergent <= tolerance,
            $"Array-path side-effecting argument ran in only one mode at {divergent} budgets " +
            $"({skippedByCompiled} where compiled skipped it) — exceeds tolerance {tolerance}.");
    }

    private static async Task<int> RunArrayPathAsync(ExecutionMode mode, int maxAllocatedBytes)
    {
        var touches = 0;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(TouchBinding(() => touches++));
            builder.AddBinding(ConsumeManyBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

        var module = await host.ImportJsonAsync(ArrayPathModuleJson);
        var policy = new SandboxPolicy(
            "touch-and-consume",
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [new CapabilityGrant("game.write", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: maxAllocatedBytes));
        var plan = await host.PrepareAsync(module, policy);

        await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return touches;
    }

    // A pure six-argument sink that forces the array-backed emit path. Compiled via the generic
    // CallBinding stub (Method == CallBinding).
    private static BindingDescriptor ConsumeManyBinding()
        => new(
            "app.consumeMany",
            SemVersion.One,
            [.. Enumerable.Repeat(SandboxType.String, 6)],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            static (_, _) => { });

    // A side-effecting, capability-gated binding that takes no arguments, records each invocation, and
    // returns a valid message target so it can stand in as the first argument to host.message.send.
    private static BindingDescriptor TouchBinding(Action record)
        => new(
            "app.touch",
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            "game.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, _, _) =>
            {
                record();
                var timestamp = context.AuditTimestamp();
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    timestamp,
                    true,
                    BindingId: "app.touch",
                    CapabilityId: "game.write",
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: "touch:test",
                    Fields: context.BindingAuditFields("touch", timestamp)));
                return ValueTask.FromResult(SandboxValue.FromString("player-1"));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            static (grant, diagnostics) =>
            {
                foreach (var key in grant.Parameters.Keys)
                {
                    diagnostics.Add(new SandboxDiagnostic(
                        "E-POLICY-GRANT-PARAM",
                        $"grant '{grant.Id}' parameter '{key}' is not supported"));
                }
            });
}
