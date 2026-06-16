using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IAsyncNumberService
{
    ValueTask<int> GetAsync();
}

public sealed class KernelRpcServiceProxyAsyncTests
{
    [Fact]
    public async Task ValueTask_service_method_returns_before_kernel_rpc_completes()
    {
        var pending = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = PluginServer.Create(
            configureHost: builder => builder.AddBinding(PendingBinding(pending)),
            defaultPolicy: AsyncPolicy());
        var kernel = await server.InstallRpcAsync(PendingNumberPackage());
        var service = KernelRpcServiceProxy.Create<IAsyncNumberService>(kernel);

        var invocation = Task.Run(() => service.GetAsync());

        try
        {
            var completed = await Task.WhenAny(invocation, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.Same(invocation, completed);
            var result = await invocation;
            Assert.False(result.IsCompleted);
            pending.SetResult(SandboxValue.FromInt32(42));
            Assert.Equal(42, await result);
        }
        finally
        {
            pending.TrySetResult(SandboxValue.FromInt32(42));
        }
    }

    private static BindingDescriptor PendingBinding(TaskCompletionSource<SandboxValue> pending)
        => new(
            "test.pendingNumber",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => new ValueTask<SandboxValue>(pending.Task),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private static SandboxPolicy AsyncPolicy()
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();

    private static PluginPackage PendingNumberPackage()
    {
        var span = new SourceSpan(1, 1);
        var function = new SandboxFunction(
            "Get",
            IsEntrypoint: true,
            [],
            SandboxType.I32,
            [new ReturnStatement(new CallExpression("test.pendingNumber", [], null, span), span)]);
        var module = new SandboxModule(
            "pending-number",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = "pending-number", ["kernel"] = "PendingNumberKernel" });
        var manifest = new PluginManifest(
            "pending-number",
            nameof(IAsyncNumberService),
            ExecutionMode.Auto,
            ["Cpu", "Concurrency"],
            [],
            [])
        {
            RequiredCapabilities = [RuntimeCapabilityIds.Async],
            RpcEntrypoint = "Get"
        };

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("Get", "Get"));
    }
}
