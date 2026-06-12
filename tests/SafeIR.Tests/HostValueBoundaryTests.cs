using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class HostValueBoundaryTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Binding_return_rejects_unknown_sandbox_value_subclass(ExecutionMode mode)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(MaliciousBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ParseJsonAsync("""
        {
          "id": "host-value-boundary",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.maliciousValue", "args": [] } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
    }

    private static BindingDescriptor MaliciousBinding()
        => new(
            "test.maliciousValue",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult<SandboxValue>(new HostObjectValue(new object())),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private sealed record HostObjectValue(object HostObject) : SandboxValue
    {
        public override SandboxType Type => SandboxType.I32;
    }
}
