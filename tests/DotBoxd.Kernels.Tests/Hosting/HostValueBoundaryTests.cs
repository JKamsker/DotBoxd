using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

public sealed class HostValueBoundaryTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    public static TheoryData<ExecutionMode, SandboxType, SandboxValue> MalformedScalars()
    {
        var data = new TheoryData<ExecutionMode, SandboxType, SandboxValue>();
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
        {
            data.Add(mode, SandboxType.F64, new F64Value(double.NaN));
            data.Add(mode, SandboxType.SandboxPath, new SandboxPathValue(new SandboxPath("../secret.txt")));
            data.Add(
                mode,
                SandboxType.Scalar("SandboxUri"),
                new SandboxUriValue(new SandboxUri("https://user:pass@example.com/config")));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MalformedScalars))]
    public async Task Entrypoint_input_rejects_malformed_scalar_record(
        ExecutionMode mode,
        SandboxType type,
        SandboxValue malformed)
    {
        var host = Host();
        var plan = await host.PrepareAsync(IdentityModule(type), SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            malformed,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
    }

    [Theory]
    [MemberData(nameof(MalformedScalars))]
    public async Task Binding_return_rejects_malformed_scalar_record(
        ExecutionMode mode,
        SandboxType type,
        SandboxValue malformed)
    {
        const string bindingId = "test.malformedScalar";
        var host = Host(MalformedScalarBinding(bindingId, type, malformed));
        var plan = await host.PrepareAsync(
            BindingReturnModule(type, bindingId),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Binding_return_rejects_unknown_sandbox_value_subclass(ExecutionMode mode)
    {
        var host = Host(MaliciousBinding());
        var module = await host.ImportJsonAsync("""
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

    private static SandboxHost Host(params BindingDescriptor[] bindings)
        => SandboxHost.Create(builder =>
        {
            foreach (var binding in bindings)
            {
                builder.AddBinding(binding);
            }

            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxModule IdentityModule(SandboxType type)
        => new(
            "host-value-boundary",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [new Parameter("value", type)],
                    type,
                    [
                        new ReturnStatement(
                            new VariableExpression("value", new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static SandboxModule BindingReturnModule(SandboxType returnType, string bindingId)
        => new(
            "host-value-boundary",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    returnType,
                    [
                        new ReturnStatement(
                            new CallExpression(bindingId, [], null, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static BindingDescriptor MalformedScalarBinding(string id, SandboxType returnType, SandboxValue value)
        => new(
            id,
            SemVersion.One,
            [],
            returnType,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(value),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

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
