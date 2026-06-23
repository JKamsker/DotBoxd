using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class CapabilityParityTestSupport
{
    public static SandboxHost CapabilityParity_MessageHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static string CapabilityParity_MessageModuleJson(string id)
        => CapabilityParity_MessageModuleJsonWithTarget(id, "player-1");

    public static string CapabilityParity_MessageModuleJsonWithTarget(string id, string targetId)
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
                    "args": [
                      { "string": "{{targetId}}" },
                      { "string": "hello" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    public static SandboxHost CapabilityParity_CounterHost(Capability_Counter counter)
        => SandboxHost.Create(builder =>
        {
            builder.AddBinding(CapabilityParity_CounterBinding(counter));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static SandboxPolicy CapabilityParity_GameWritePolicy()
        => new(
            "game-write",
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [new CapabilityGrant("game.write", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

    public static string CapabilityParity_CounterModuleJson(string id)
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
              "body": [{ "op": "return", "value": { "call": "app.counter", "args": [{ "i32": 7 }] } }]
            }
          ]
        }
        """;

    private static BindingDescriptor CapabilityParity_CounterBinding(Capability_Counter counter)
        => new(
            "app.counter",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            "game.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, args, _) =>
            {
                var value = ((I32Value)args[0]).Value;
                counter.Add(value);
                var timestamp = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    timestamp,
                    true,
                    BindingId: "app.counter",
                    CapabilityId: "game.write",
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: "counter:test",
                    Fields: context.BindingAuditFields("counter", timestamp)));
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            CapabilityParity_NoParameterGrant);

    private static void CapabilityParity_NoParameterGrant(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter '{key}' is not supported"));
        }
    }
}

internal sealed class Capability_Counter
{
    private int _total;

    public int TotalIncrement => _total;

    public void Add(int value) => Interlocked.Add(ref _total, value);
}

internal sealed class Capability_InMemoryPluginMessageSink : IPluginMessageSink
{
    private readonly InMemoryPluginMessageSink _inner = new();

    public IReadOnlyList<PluginMessage> Messages => _inner.Messages;

    public ValueTask SendAsync(
        string targetId,
        string message,
        CancellationToken cancellationToken = default)
        => _inner.SendAsync(targetId, message, cancellationToken);
}
