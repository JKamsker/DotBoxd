using DotBoxd.Hosting;
using DotBoxd.Kernels.Runtime;

namespace DotBoxd.Kernels.Tests;

public sealed class ImportedHostRuntimeBoundaryTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Cancellation_is_observed_after_straight_line_statement(ExecutionMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(CancelBinding(cancellation.Cancel));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(StraightLineCancelModule());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false
            },
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Precancelled_token_returns_cancelled_result(ExecutionMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SingleReturnModule());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false
            },
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Cancellation_is_observed_at_loop_back_edge(ExecutionMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(CancelBinding(cancellation.Cancel));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(LoopCancelModule());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false
            },
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Host_binding_base_cost_is_charged_before_dispatch(ExecutionMode mode)
    {
        var calls = 0;
        var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(CostlyBinding(() => calls++));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ReturnExpressionModule(
            """{ "call": "test.expensive", "args": [] }""",
            "I32"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(5).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(0, calls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_loop_fuel_exhaustion_returns_quota_exceeded()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(InfiniteLoopModule());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(50).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                AllowFallbackToInterpreter = false
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static BindingDescriptor CancelBinding(Action cancel)
        => new(
            "test.cancel",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                cancel();
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor CostlyBinding(Action invoke)
        => new(
            "test.expensive",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(10),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                invoke();
                return ValueTask.FromResult(SandboxValue.FromInt32(1));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string StraightLineCancelModule()
        => """
        {
          "id": "straight-line-cancel",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "expr", "value": { "call": "test.cancel", "args": [] } },
                { "op": "set", "name": "value", "value": { "i32": 1 } },
                { "op": "return", "value": { "var": "value" } }
              ]
            }
          ]
        }
        """;

    private static string LoopCancelModule()
        => """
        {
          "id": "loop-cancel",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "while",
                  "condition": { "bool": true },
                  "body": [
                    { "op": "expr", "value": { "call": "test.cancel", "args": [] } }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

    private static string InfiniteLoopModule()
        => """
        {
          "id": "compiled-infinite-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "while",
                  "condition": { "bool": true },
                  "body": [
                    { "op": "set", "name": "x", "value": { "i32": 1 } }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

    private static string SingleReturnModule()
        => ReturnExpressionModule("""{ "i32": 1 }""", "I32");

    private static string ReturnExpressionModule(string expression, string returnType)
        => $$"""
        {
          "id": "host-runtime-boundary",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;
}
