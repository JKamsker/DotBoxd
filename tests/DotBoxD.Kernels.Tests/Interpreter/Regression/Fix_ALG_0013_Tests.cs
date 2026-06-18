using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression;

/// <summary>
/// Regression coverage for ALG-0013: <see cref="SandboxHost.ExecuteAsync"/> calls
/// <c>ExecutionPlanGuard.EnsurePrepared</c> at the start of every run, which re-validates the
/// whole module (a fresh <c>ModuleValidator().Validate(...)</c>), re-canonical-hashes the module,
/// and rebuilds + reseals the expected execution plan on EVERY execution of an already-prepared
/// plan. That per-run integrity work scales with module size (function count, statements, binding
/// references, ordered seal records) instead of with the selected entrypoint's execution cost.
///
/// The observable wrong behavior: take two plans that are prepared once and whose entrypoints do
/// the same trivial constant work, but whose modules differ only in how many OTHER pure functions
/// they contain. Run the same trivial entrypoint many times through the real execute path and
/// measure host-side allocation per call (the interpreter does identical work for both, so any
/// per-run growth is the size-dependent guard rebuild). Today the large module allocates far more
/// host memory per execution than the small one because the guard re-hashes/re-validates/reseals
/// the entire module on each run.
///
/// These tests pin the CORRECT post-fix contract: once a plan is prepared, the per-run integrity
/// check must be O(1) against immutable prepared-plan identity, so repeated execution of the same
/// prepared plan must NOT scale with total module size. They are therefore RED until the guard
/// stops repeating the full prepare-time validate/seal pipeline on every dispatch.
/// </summary>
public sealed class Fix_ALG_0013_Tests
{
    // The large module has many extra pure functions the entrypoint never touches; the small
    // module has just the entrypoint. The entrypoints are byte-identical, so the only thing that
    // differs at execute time is how much module the per-run integrity guard rebuilds.
    private const int SmallExtraFunctions = 0;
    private const int LargeExtraFunctions = 250;

    // Iterations to average away one-off noise (boxing of the loop, async state machine, etc.).
    private const int Iterations = 200;

    // Correct behavior: per-run host allocation is independent of module size, so the large
    // module must stay within a small constant factor of the small one. With the per-run full
    // revalidation/reseal present today, the large module allocates many multiples more per run
    // (it canonical-hashes and reseals 250 extra functions every single execution), so this
    // ceiling fails until the guard becomes O(1).
    private const double MaxPerRunGrowthFactor = 3.0;

    [Fact]
    public async Task Repeated_execution_does_not_scale_host_allocation_with_module_size()
    {
        var smallPerRun = await MeasurePerRunAllocationAsync(SmallExtraFunctions);
        var largePerRun = await MeasurePerRunAllocationAsync(LargeExtraFunctions);

        // Guard against a degenerate near-zero small baseline making the ratio meaningless.
        Assert.True(smallPerRun > 0, "expected non-zero per-run host allocation to compare against");

        var growth = (double)largePerRun / smallPerRun;
        Assert.True(
            growth < MaxPerRunGrowthFactor,
            $"ExecuteAsync allocated ~{largePerRun} bytes/run for a module with " +
            $"{LargeExtraFunctions} extra functions vs ~{smallPerRun} bytes/run for the same " +
            $"entrypoint in a {SmallExtraFunctions}-extra-function module ({growth:0.00}x). " +
            "Per-run cost scales with module size because the integrity guard re-validates, " +
            "re-canonical-hashes, and reseals the whole prepared plan on every execution.");
    }

    private static async Task<long> MeasurePerRunAllocationAsync(int extraFunctions)
    {
        using var host = BuildHost();
        var module = BuildPureModule(extraFunctions);
        var plan = await host.PrepareAsync(module, PurePolicy());

        // Warm up: JIT the execute path, prime caches, and surface first-call allocations so the
        // measured loop reflects steady-state per-run cost only.
        for (var i = 0; i < 10; i++)
        {
            var warm = await ExecuteOnceAsync(host, plan);
            Assert.True(warm.Succeeded, warm.Error?.SafeMessage);
            Assert.Equal(350, ((I32Value)warm.Value!).Value);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            _ = await ExecuteOnceAsync(host, plan);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return allocated / Iterations;
    }

    private static ValueTask<SandboxExecutionResult> ExecuteOnceAsync(SandboxHost host, ExecutionPlan plan)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
            });

    // Interpreter-only host with pure bindings; no compiler so both module sizes take the exact
    // same minimal dispatch path and the only size-dependent host work is the integrity guard.
    private static SandboxHost BuildHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .AllowPureComputation()
            .WithFuel(1_000_000)
            .WithMaxAllocatedBytes(10_000_000)
            .Build();

    // The entrypoint is identical for every module size: base = level*10, bonus = rarity*25,
    // return base + bonus. Called with level=0,rarity=0 via a literal-folded body so the result
    // is a fixed 350 regardless of input. "extraFunctions" pure functions are appended; the
    // entrypoint never calls them, so they only inflate the module the guard rebuilds per run.
    private static SandboxModule BuildPureModule(int extraFunctions)
    {
        var span = new SourceSpan(0, 0);
        var functions = new List<SandboxFunction> { Entrypoint(span) };
        for (var i = 0; i < extraFunctions; i++)
        {
            functions.Add(ExtraFunction(i, span));
        }

        return new SandboxModule(
            $"alg-0013-{extraFunctions}",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            functions,
            new Dictionary<string, string>());
    }

    private static SandboxFunction Entrypoint(SourceSpan span)
        => new(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                new AssignmentStatement(
                    "base",
                    Mul(Literal(10, span), Literal(10, span), span),
                    span),
                new AssignmentStatement(
                    "bonus",
                    Mul(Literal(10, span), Literal(25, span), span),
                    span),
                new ReturnStatement(
                    Add(new VariableExpression("base", span), new VariableExpression("bonus", span), span),
                    span),
            ]);

    private static SandboxFunction ExtraFunction(int index, SourceSpan span)
        => new(
            $"helper_{index:D4}",
            false,
            [new Parameter("x", SandboxType.I32)],
            SandboxType.I32,
            [
                new AssignmentStatement(
                    "y",
                    Add(new VariableExpression("x", span), Literal(index + 1, span), span),
                    span),
                new ReturnStatement(
                    Mul(new VariableExpression("y", span), Literal(2, span), span),
                    span),
            ]);

    private static LiteralExpression Literal(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);

    private static BinaryExpression Add(Expression left, Expression right, SourceSpan span)
        => new(left, "+", right, span);

    private static BinaryExpression Mul(Expression left, Expression right, SourceSpan span)
        => new(left, "*", right, span);
}
