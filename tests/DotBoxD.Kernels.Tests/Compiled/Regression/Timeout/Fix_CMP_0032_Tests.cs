using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Timeout;

public sealed class Fix_CMP_0032_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Sync_binding_returning_after_wall_deadline_fails_with_timeout(ExecutionMode mode)
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(SlowBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithWallTime(TimeSpan.FromMilliseconds(10))
            .WithMaxHostCalls(2)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    private static BindingDescriptor SlowBinding()
        => new(
            "test.slow",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(75));
                return ValueTask.FromResult(SandboxValue.FromInt32(7));
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private static string ModuleJson()
        => """
        {
          "id": "binding-wall-deadline",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "test.slow", "args": [] } }
              ]
            }
          ]
        }
        """;
}
