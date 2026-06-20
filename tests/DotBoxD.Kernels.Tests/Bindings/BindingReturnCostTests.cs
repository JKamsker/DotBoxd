using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxContext = DotBoxD.Kernels.Sandbox.SandboxContext;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingReturnCostTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_return_allocation_cost_enforces_string_limits(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            StringBinding("test.largeString", "hello", perByteFuel: 0),
            SandboxPolicyBuilder.Create()
                .WithMaxStringLength(4)
                .WithFuel(1_000)
                .Build(),
            mode,
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_return_per_byte_cost_charges_fuel(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            StringBinding("test.largeString", "hello", perByteFuel: 10),
            SandboxPolicyBuilder.Create()
                .WithMaxStringLength(64)
                .WithFuel(50)
                .Build(),
            mode,
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_per_byte_cost_charges_fuel_without_allocation_flag(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            PerByteStringBinding("test.perByteString", "hello", perByteFuel: 10),
            SandboxPolicyBuilder.Create()
                .WithMaxStringLength(64)
                .WithFuel(50)
                .Build(),
            mode,
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_return_shape_limits_apply_without_allocation_flag(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            PerByteStringBinding("test.unmeteredString", "hello", perByteFuel: 0),
            SandboxPolicyBuilder.Create()
                .WithMaxStringLength(4)
                .WithFuel(1_000)
                .Build(),
            mode,
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Precharged_string_binding_return_is_not_charged_twice(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            PrechargedStringBinding("test.prechargedString", "hello"),
            SandboxPolicyBuilder.Create()
                .WithMaxStringLength(64)
                .WithMaxTotalStringBytes(10)
                .WithFuel(1_000)
                .Build(),
            mode,
            compiler);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(10, result.ResourceUsage.StringBytes);
        Assert.Equal("hello", ((StringValue)result.Value!).Value);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_return_deep_collection_type_mismatch_is_sanitized(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            BadListBinding(),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build(),
            mode,
            compiler,
            """{ "name": "List", "arguments": ["I32"] }""");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_return_scalar_invariants_are_sanitized(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            BadPathBinding(),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build(),
            mode,
            compiler,
            "\"SandboxPath\"");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Binding_return_large_collection_is_charged_from_single_shape_scan(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteAsync(
            ListBinding(256),
            SandboxPolicyBuilder.Create()
                .WithMaxTotalCollectionElements(256)
                .WithFuel(1_000)
                .Build(),
            mode,
            compiler,
            """{ "name": "List", "arguments": ["I32"] }""");

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(256, result.ResourceUsage.CollectionElements);
    }

    [Fact]
    public async Task Binding_return_type_mismatch_is_sanitized()
    {
        var result = await ExecuteAsync(
            new BindingDescriptor(
                "test.badType",
                SemVersion.One,
                [],
                SandboxType.String,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(1)),
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding))),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build(),
            ExecutionMode.Interpreted,
            compiler: false);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
    }

    [Fact]
    public void Binding_return_credit_recorder_is_not_public_binding_api()
    {
        var publicMethods = typeof(SandboxContext)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        Assert.DoesNotContain(publicMethods, method => method.Name == "RecordStringReturnCredit");
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        BindingDescriptor binding,
        SandboxPolicy policy,
        ExecutionMode mode,
        bool compiler,
        string returnType = "\"String\"")
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(binding);
            builder.UseInterpreter();
            if (compiler)
            {
                builder.UseCompilerIfAvailable();
            }
        });
        var module = await host.ImportJsonAsync(ReturnBindingJson(binding.Id, returnType));
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static BindingDescriptor StringBinding(string id, string value, long perByteFuel)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            null,
            new BindingCostModel(1, perByteFuel, AllocationFromReturnBytes: true),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromString(value)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static BindingDescriptor PerByteStringBinding(string id, string value, long perByteFuel)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.PerByte(1, perByteFuel),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromString(value)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static BindingDescriptor PrechargedStringBinding(string id, string value)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (context, _, _) =>
            {
                context.ChargeString(value);
                return ValueTask.FromResult(SandboxValue.FromString(value));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static BindingDescriptor BadListBinding()
        => new(
            "test.badList",
            SemVersion.One,
            [],
            SandboxType.List(SandboxType.I32),
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult<SandboxValue>(
                new ListValue([SandboxValue.FromString("wrong")], SandboxType.I32)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static BindingDescriptor BadPathBinding()
        => new(
            "test.badPath",
            SemVersion.One,
            [],
            SandboxType.SandboxPath,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult<SandboxValue>(
                new SandboxPathValue(new SandboxPath("../secret.txt"))),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static BindingDescriptor ListBinding(int count)
        => new(
            "test.list",
            SemVersion.One,
            [],
            SandboxType.List(SandboxType.I32),
            SandboxEffect.Cpu | SandboxEffect.Alloc,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult<SandboxValue>(
                SandboxValue.FromList(Enumerable.Range(0, count).Select(SandboxValue.FromInt32).ToArray())),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private static string ReturnBindingJson(string bindingId, string returnType)
        => $$"""
        {
          "id": "binding-return-cost",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": {{returnType}},
              "body": [{ "op": "return", "value": { "call": "{{bindingId}}", "args": [] } }]
            }
          ]
        }
        """;
}
