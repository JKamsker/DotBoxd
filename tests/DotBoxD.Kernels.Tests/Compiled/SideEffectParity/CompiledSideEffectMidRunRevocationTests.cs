using DotBoxD.Hosting;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Closes the "compiled artifact must not cache a stale capability grant" concern at the finest
/// granularity: a capability revoked DURING a run (a side-effecting argument revokes host.message.write,
/// then host.message.send is dispatched in the same execution). Whatever the interpreter does with a
/// mid-run revocation, compiled must do identically — it must not let a binding through on a grant it
/// cached when the run started. Complements the cross-dispatch revoke-before-execute parity test in
/// CompiledPluginMessageBindingTests.
/// </summary>
public sealed class CompiledSideEffectMidRunRevocationTests
{
    private const string ModuleJson = """
    {
      "id": "mid-run-revocation",
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
                  { "call": "app.revoke", "args": [] },
                  { "string": "should be blocked" }
                ]
              }
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public async Task Capability_revoked_mid_run_is_enforced_identically_in_both_modes()
    {
        var (interpreted, interpretedSink) = await RunAsync(ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await RunAsync(ExecutionMode.Compiled);

        // Parity is the guarantee: compiled must not diverge from the interpreter on a capability
        // revoked between binding calls within a single run — it must not enforce capability more
        // leniently (by caching a stale grant) nor more strictly than the interpreter.
        Assert.Equal(interpreted.Succeeded, compiled.Succeeded);
        Assert.Equal(interpreted.Error?.Code, compiled.Error?.Code);
        Assert.Equal(interpretedSink, compiledSink);

        // Observed model: capability is evaluated against the snapshot taken when the dispatch starts,
        // so revoking host.message.write between binding calls within the SAME run does not retroactively
        // block the in-flight host.message.send — and compiled matches the interpreter exactly. (A
        // revocation taking effect on the NEXT dispatch is covered by the cross-dispatch parity test in
        // CompiledPluginMessageBindingTests.) If capability checking ever becomes per-call/live, these
        // assertions flip and force a conscious review of compiled-vs-interpreted parity at that point.
        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(1, interpretedSink);
        Assert.Equal(1, compiledSink);
    }

    private static async Task<(SandboxExecutionResult Result, int SinkCount)> RunAsync(ExecutionMode mode)
    {
        var holder = new SandboxHost[1];
        var sink = new InMemoryPluginMessageSink();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddBinding(RevokeBinding(() => holder[0]));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        holder[0] = host;

        var module = await host.ImportJsonAsync(ModuleJson);
        var policy = new SandboxPolicy(
            "revoke-mid-run",
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [
                new CapabilityGrant("host.message.write", new Dictionary<string, string>()),
                new CapabilityGrant("game.write", new Dictionary<string, string>())
            ],
            new ResourceLimits(MaxFuel: 1_000_000));
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink.Messages.Count);
    }

    // A side-effecting, capability-gated binding that revokes host.message.write when invoked, then
    // returns a valid message target. Used as the first argument to host.message.send so the revocation
    // happens mid-run, before the outer binding is dispatched.
    private static BindingDescriptor RevokeBinding(Func<SandboxHost> host)
        => new(
            "app.revoke",
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
                host().RevokeCapability(PluginMessageBindings.CapabilityId, "revoked mid-run");
                var timestamp = context.AuditTimestamp();
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    timestamp,
                    true,
                    BindingId: "app.revoke",
                    CapabilityId: "game.write",
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: "revoke:test",
                    Fields: context.BindingAuditFields("revoke", timestamp)));
                return ValueTask.FromResult(SandboxValue.FromString("player-1"));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            static (_, _) => { });
}
