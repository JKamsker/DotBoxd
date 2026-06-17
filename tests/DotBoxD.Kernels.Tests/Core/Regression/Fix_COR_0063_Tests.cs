using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Core.Regression;

public sealed class Fix_COR_0063_Tests
{
    private const string BindingId = "host.concurrent.value";
    private const string CapabilityId = "host.concurrent.value";

    [Fact]
    public async Task Prepare_requires_runtime_async_for_synchronous_concurrency_effect_binding()
    {
        using var host = CreateHost();
        var module = await host.ImportJsonAsync(CallConcurrentBindingModuleJson());
        var policy = SandboxPolicyBuilder.Create()
            .Grant(CapabilityId, new { }, SandboxEffect.Concurrency)
            .WithFuel(1_000)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, policy));

        Assert.Contains(
            ex.Diagnostics,
            d => d.Code == "E-POLICY-CAP" &&
                 d.Message.Contains(RuntimeCapabilityIds.Async, StringComparison.Ordinal));
    }

    private static SandboxHost CreateHost()
        => SandboxHost.Create(builder => {
            builder.AddBinding(ConcurrentBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static BindingDescriptor ConcurrentBinding()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.Concurrency,
            CapabilityId,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(7)),
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)),
            GrantValidator: static (_, _) => { });

    private static string CallConcurrentBindingModuleJson()
        => $$"""
        {
          "id": "sync-concurrency-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "{{BindingId}}", "args": [] } }]
            }
          ]
        }
        """;
}
