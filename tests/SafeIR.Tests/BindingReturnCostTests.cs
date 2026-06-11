using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

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
                CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding))),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build(),
            ExecutionMode.Interpreted,
            compiler: false);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        BindingDescriptor binding,
        SandboxPolicy policy,
        ExecutionMode mode,
        bool compiler)
    {
        var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddBinding(binding);
            builder.UseInterpreter();
            if (compiler) {
                builder.UseCompilerIfAvailable();
            }
        });
        var module = await host.ParseJsonAsync(ReturnBindingJson(binding.Id));
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
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

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
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string ReturnBindingJson(string bindingId)
        => $$"""
        {
          "id": "binding-return-cost",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [{ "op": "return", "value": { "call": "{{bindingId}}", "args": [] } }]
            }
          ]
        }
        """;
}
