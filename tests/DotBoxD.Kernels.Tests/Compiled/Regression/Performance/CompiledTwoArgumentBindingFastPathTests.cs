using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class CompiledTwoArgumentBindingFastPathTests
{
    [Fact]
    public async Task Compiled_two_argument_binding_uses_fast_invoker_without_argument_list()
    {
        var invoker = new FastAddBinding();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(invoker.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(AddModuleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(42, ((I32Value)result.Value!).Value);
        Assert.Equal(1, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    [Fact]
    public async Task Compiled_two_argument_binding_falls_back_to_regular_invoker()
    {
        var invoker = new RegularAddBinding();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(invoker.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(AddModuleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, ((I32Value)result.Value!).Value);
        Assert.Equal(1, invoker.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Charge_value_array_matches_create_value_array_accounting(int count)
    {
        var charged = Context();
        var created = Context();

        Kernels.Runtime.CompiledRuntime.ChargeValueArray(charged, count);
        _ = Kernels.Runtime.CompiledRuntime.CreateValueArray(created, count);

        Assert.Equal(created.Budget.FuelUsed, charged.Budget.FuelUsed);
        Assert.Equal(created.Budget.AllocatedBytes, charged.Budget.AllocatedBytes);
    }

    [Fact]
    public void Create_literal_value_array_zero_reuses_empty_array()
    {
        var first = Kernels.Runtime.CompiledRuntime.CreateLiteralValueArray(0);
        var second = Kernels.Runtime.CompiledRuntime.CreateLiteralValueArray(0);

        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Compiled_binding_validation_accepts_structural_arguments()
    {
        var invoked = false;
        var binding = StructuralBinding(
            "test.structural",
            [
                SandboxType.List(SandboxType.I32),
                SandboxType.Record([SandboxType.Map(SandboxType.String, SandboxType.I32)])
            ],
            () => invoked = true);
        var context = Context(binding);
        var list = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1)
            },
            SandboxType.String,
            SandboxType.I32);
        var record = SandboxValue.FromRecord([map]);

        var result = Kernels.Runtime.CompiledRuntime.CallBinding2(context, binding.Id, list, record);

        Assert.Same(SandboxValue.Unit, result);
        Assert.True(invoked);
    }

    [Fact]
    public void Compiled_binding_validation_rejects_structural_mismatch_before_host_invoke()
    {
        var invoked = false;
        var binding = StructuralBinding(
            "test.list64",
            [SandboxType.List(SandboxType.I64)],
            () => invoked = true);
        var context = Context(binding);
        var wrong = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Kernels.Runtime.CompiledRuntime.CallBinding(context, binding.Id, [wrong]));

        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.False(invoked);
    }

    private const string AddModuleJson = """
    {
      "id": "compiled-two-arg-binding-fast-path",
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
              "value": { "call": "test.add2", "args": [{ "i32": 20 }, { "i32": 22 }] }
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

    private static BindingDescriptor StructuralBinding(
        string id,
        IReadOnlyList<SandboxType> parameters,
        Action invoked)
        => new(
            id,
            SemVersion.One,
            parameters,
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoked();
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private sealed class FastAddBinding : ITwoArgumentBindingInvoker
    {
        public int FastCalls { get; private set; }
        public int ListCalls { get; private set; }

        public BindingDescriptor Descriptor()
            => new(
                "test.add2",
                SemVersion.One,
                [SandboxType.I32, SandboxType.I32],
                SandboxType.I32,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Add(args[0], args[1]);
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return Add(arg0, arg1);
        }

        private static ValueTask<SandboxValue> Add(SandboxValue arg0, SandboxValue arg1)
            => ValueTask.FromResult(SandboxValue.FromInt32(((I32Value)arg0).Value + ((I32Value)arg1).Value));
    }

    private sealed class RegularAddBinding
    {
        public int Calls { get; private set; }

        public BindingDescriptor Descriptor()
            => new(
                "test.add2",
                SemVersion.One,
                [SandboxType.I32, SandboxType.I32],
                SandboxType.I32,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            Calls++;
            var left = ((I32Value)args[0]).Value;
            var right = ((I32Value)args[1]).Value;
            return ValueTask.FromResult(SandboxValue.FromInt32(left + right));
        }
    }
}
