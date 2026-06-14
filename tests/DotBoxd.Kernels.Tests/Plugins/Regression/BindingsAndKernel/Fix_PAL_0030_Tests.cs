using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Hosting.Internal;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0030: <see cref="AutoExecutionHotness"/> must not
/// retain one <c>AutoHotnessState</c> per <c>planHash|entrypoint</c> for the lifetime
/// of the host. The table is bounded with an LRU policy so a long-lived host that
/// keeps preparing new plans stays proportional to recently active plan-entrypoints
/// rather than every plan hash ever seen. Active entries must keep accumulating
/// history exactly as before; only the least-recently-used entries are dropped.
/// </summary>
public sealed class Fix_PAL_0030_Tests
{
    [Fact]
    public async Task Hotness_table_is_bounded_when_many_unique_plans_are_seen()
    {
        const int maxEntries = 8;
        var hotness = new AutoExecutionHotness(maxEntries);
        var template = await PrepareTemplatePlanAsync();

        for (var i = 0; i < maxEntries * 100; i++)
        {
            hotness.BeginAttempt(WithPlanHash(template, $"plan-{i}"), "main");
        }

        Assert.Equal(maxEntries, hotness.Count);
    }

    [Fact]
    public async Task Hotness_table_retains_recently_used_entries_over_cold_ones()
    {
        const int maxEntries = 4;
        var hotness = new AutoExecutionHotness(maxEntries);
        var template = await PrepareTemplatePlanAsync();
        var hotPlan = WithPlanHash(template, "hot-plan");

        // Establish two attempts of accumulated history for the hot plan.
        hotness.BeginAttempt(hotPlan, "main");

        // Flood the table with cold plans, keeping the hot plan touched between each
        // batch so it stays the most-recently-used entry and survives eviction.
        for (var i = 0; i < maxEntries * 50; i++)
        {
            hotness.BeginAttempt(WithPlanHash(template, $"cold-{i}"), "main");
            hotness.BeginAttempt(hotPlan, "main");
        }

        Assert.Equal(maxEntries, hotness.Count);

        // The hot entry must still carry its accumulated run history rather than a
        // freshly recreated state, proving it was never evicted.
        var attempt = hotness.BeginAttempt(hotPlan, "main");
        Assert.Equal("hot-plan", attempt.Stats.PlanHash);
        Assert.True(
            attempt.Stats.RunCount > maxEntries * 50,
            $"expected retained run history but RunCount was {attempt.Stats.RunCount}");
    }

    [Fact]
    public async Task Distinct_entrypoints_are_tracked_separately()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var plan = WithPlanHash(await PrepareTemplatePlanAsync(), "shared-plan");

        var first = hotness.BeginAttempt(plan, "alpha");
        var second = hotness.BeginAttempt(plan, "beta");

        Assert.Equal("alpha", first.Stats.Entrypoint);
        Assert.Equal("beta", second.Stats.Entrypoint);
        Assert.Equal(2, hotness.Count);
    }

    [Fact]
    public void Rejects_non_positive_capacity()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AutoExecutionHotness(maxEntries: 0));

    private static async Task<ExecutionPlan> PrepareTemplatePlanAsync()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    // A fresh ExecutionPlan that mirrors the prepared template but uses a distinct
    // plan hash, so each call produces a new logical plan-entrypoint key without
    // re-running the full prepare pipeline for thousands of variants.
    private static ExecutionPlan WithPlanHash(ExecutionPlan template, string planHash)
        => new(
            template.ModuleHash,
            planHash,
            template.PlanSeal,
            template.PolicyHash,
            template.BindingManifestHash,
            template.Module,
            template.Policy,
            template.Bindings,
            template.Budget,
            template.FunctionAnalysis,
            template.BindingReferences);
}
