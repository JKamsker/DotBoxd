using SafeIR;
using SafeIR.Compiler;

namespace SafeIR.Tests;

public sealed class DeterministicExecutionOptionTests
{
    [Fact]
    public async Task Require_deterministic_rejects_non_deterministic_policy()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, RequireDeterministic = true });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "PolicyDenied" && !e.Success);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.PolicyDenied, summary.ErrorCode);
        Assert.Equal("Interpreted", summary.Fields!["mode"]);
    }

    [Fact]
    public async Task Require_deterministic_allows_deterministic_policy()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 1)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, RequireDeterministic = true });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Require_deterministic_denies_before_compilation()
    {
        var compiler = new CountingCompiler();
        var host = SandboxHostForCompiler(compiler);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, RequireDeterministic = true });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(0, compiler.Calls);
        Assert.Null(result.ArtifactHash);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal("Compiled", summary.Fields!["mode"]);
    }

    private static Hosting.SandboxHost SandboxHostForCompiler(ISandboxCompiler compiler)
        => Hosting.SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });

    private sealed class CountingCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("compiler should not be called");
        }
    }
}
