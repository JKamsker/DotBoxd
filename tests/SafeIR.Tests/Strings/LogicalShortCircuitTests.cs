using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed partial class LogicalShortCircuitTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task And_skips_right_operand_when_left_is_false(ExecutionMode mode)
    {
        var (result, calls) = await ExecuteAsync(
            """{ "op": "and", "left": { "bool": false }, "right": { "call": "test.bool", "args": [] } }""",
            bindingReturn: true,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(0, calls);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Or_skips_right_operand_when_left_is_true(ExecutionMode mode)
    {
        var (result, calls) = await ExecuteAsync(
            """{ "op": "or", "left": { "bool": true }, "right": { "call": "test.bool", "args": [] } }""",
            bindingReturn: false,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
        Assert.Equal(0, calls);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task And_evaluates_right_operand_when_left_is_true(ExecutionMode mode)
    {
        var (result, calls) = await ExecuteAsync(
            """{ "op": "and", "left": { "bool": true }, "right": { "call": "test.bool", "args": [] } }""",
            bindingReturn: false,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task And_preserves_source_order_for_pure_binding_operands(ExecutionMode mode)
    {
        var (result, calls) = await ExecuteAsync(
            """{ "op": "and", "left": { "call": "test.bool", "args": [] }, "right": { "bool": false } }""",
            bindingReturn: true,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Or_evaluates_right_operand_when_left_is_false(ExecutionMode mode)
    {
        var (result, calls) = await ExecuteAsync(
            """{ "op": "or", "left": { "bool": false }, "right": { "call": "test.bool", "args": [] } }""",
            bindingReturn: true,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Or_preserves_source_order_for_pure_binding_operands(ExecutionMode mode)
    {
        var (result, calls) = await ExecuteAsync(
            """{ "op": "or", "left": { "call": "test.bool", "args": [] }, "right": { "bool": true } }""",
            bindingReturn: false,
            mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task And_preserves_source_order_for_collection_intrinsic(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson("""
        {
          "op": "and",
          "left": {
            "op": "eq",
            "left": {
              "call": "list.count",
              "args": [
                {
                  "call": "list.of",
                  "args": [{ "i32": 1 }, { "i32": 2 }]
                }
              ]
            },
            "right": { "i32": 2 }
          },
          "right": { "bool": false }
        }
        """));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithMaxListLength(1)
                .WithFuel(1_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Fact]
    public async Task Cheap_first_does_not_reorder_side_effecting_operands()
    {
        var calls = 0;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(EffectfulBoolBinding(() => calls++));
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(ModuleJson(
            """{ "op": "and", "left": { "call": "test.effectfulBool", "args": [] }, "right": { "bool": false } }"""));
        var plan = await host.PrepareAsync(module, GameReadPolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    private static async Task<(SandboxExecutionResult Result, int Calls)> ExecuteAsync(
        string expression,
        bool bindingReturn,
        ExecutionMode mode)
    {
        var calls = 0;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(BoolBinding(() =>
            {
                calls++;
                return bindingReturn;
            }));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson(expression));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false
            });

        return (result, calls);
    }

    private static BindingDescriptor BoolBinding(
        Func<bool> invoke,
        BindingSafety safety = BindingSafety.PureIntrinsic)
        => new(
            "test.bool",
            SemVersion.One,
            [],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            safety,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromBool(invoke())),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor EffectfulBoolBinding(Action invoke)
        => new(
            "test.effectfulBool",
            SemVersion.One,
            [],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            "game.read",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, _, _) =>
            {
                invoke();
                var timestamp = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    timestamp,
                    true,
                    BindingId: "test.effectfulBool",
                    CapabilityId: "game.read",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: "game:test",
                    Fields: context.BindingAuditFields("game", timestamp)));
                return ValueTask.FromResult(SandboxValue.FromBool(true));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            NoParameterGrant);

    private static SandboxPolicy GameReadPolicy()
        => new(
            "game-read",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            [new CapabilityGrant("game.read", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

    private static void NoParameterGrant(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter '{key}' is not supported"));
        }
    }

    private static string ModuleJson(string expression)
        => $$"""
        {
          "id": "logical-short-circuit",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Bool",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;

}
