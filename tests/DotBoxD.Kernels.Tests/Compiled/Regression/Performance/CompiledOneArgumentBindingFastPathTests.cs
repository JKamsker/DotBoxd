using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class CompiledOneArgumentBindingFastPathTests
{
    [Fact]
    public async Task Compiled_one_argument_binding_uses_fast_invoker_without_argument_list()
    {
        var invoker = new FastIdentityBinding();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(invoker.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(IdentityModuleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(41, ((I32Value)result.Value!).Value);
        Assert.Equal(1, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    [Fact]
    public async Task Compiled_one_argument_binding_falls_back_to_regular_invoker()
    {
        var invoker = new RegularIdentityBinding();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(invoker.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(IdentityModuleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(41, ((I32Value)result.Value!).Value);
        Assert.Equal(1, invoker.Calls);
    }

    [Fact]
    public void Compiled_single_argument_binding_validation_rejects_structural_mismatch_before_host_invoke()
    {
        var invoked = false;
        var binding = new BindingDescriptor(
            "test.list64",
            SemVersion.One,
            [SandboxType.List(SandboxType.I64)],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
        var context = Context(binding);
        var wrong = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Kernels.Runtime.CompiledRuntime.CallBinding1(context, binding.Id, wrong));

        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.False(invoked);
    }

    private const string IdentityModuleJson = """
    {
      "id": "compiled-one-arg-binding-fast-path",
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
              "value": { "call": "test.identity", "args": [{ "i32": 41 }] }
            }
          ]
        }
      ]
    }
    """;

    private static SandboxContext Context(params BindingDescriptor[] bindings)
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().AddRange(bindings).Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private sealed class FastIdentityBinding : IOneArgumentBindingInvoker
    {
        public int FastCalls { get; private set; }
        public int ListCalls { get; private set; }

        public BindingDescriptor Descriptor()
            => new(
                "test.identity",
                SemVersion.One,
                [SandboxType.I32],
                SandboxType.I32,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Identity(args[0]);
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return Identity(arg0);
        }
    }

    private sealed class RegularIdentityBinding
    {
        public int Calls { get; private set; }

        public BindingDescriptor Descriptor()
            => new(
                "test.identity",
                SemVersion.One,
                [SandboxType.I32],
                SandboxType.I32,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Identity(args[0]);
        }
    }

    private static ValueTask<SandboxValue> Identity(SandboxValue arg0) =>
        ValueTask.FromResult(arg0);
}
