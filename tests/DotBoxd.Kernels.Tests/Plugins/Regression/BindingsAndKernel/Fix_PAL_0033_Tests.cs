using DotBoxd.Kernels.PluginIpc.Server.Abstractions;
using DotBoxd.Kernels.PluginLocal;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0033: an installed plugin kernel must not retain
/// one execution observation per run forever. The observer appends a record for
/// every <c>ShouldHandle</c> and <c>Handle</c> sandbox execution and never evicts
/// old entries, so <see cref="InstalledKernel.ExecutionObservations"/> grows 1:1
/// with the number of events a long-running host processes and every read copies
/// the entire retained history.
///
/// The correct, post-fix behavior the finding describes is that full history is
/// bounded (a ring buffer or opt-in window) while <c>LastExecution</c> stays the
/// always-on hot-path diagnostic. These tests encode that expectation, so they are
/// red until retention becomes bounded.
/// </summary>
public sealed class Fix_PAL_0033_Tests
{
    // A generous upper ceiling: any reasonable bounded ring buffer or opt-in
    // history default sits well below this, while the current unbounded list
    // blows straight past it once the host has processed enough events.
    private const int RetentionCeiling = 256;

    [Fact]
    public async Task ExecutionObservations_stay_bounded_across_many_runs()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(
            messages,
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        // Each matching publish drives one ShouldHandle plus one Handle execution,
        // so 300 events produce 600 recorded observations under unbounded retention.
        const int eventCount = 300;
        for (var i = 0; i < eventCount; i++)
        {
            await server.Hooks.PublishAsync(new DamageEvent("fire", 120, $"player-{i}"));
        }

        var retained = kernel.ExecutionObservations.Count;

        // Always-on last-execution diagnostic must still reflect the final run.
        Assert.NotNull(kernel.LastExecution);
        Assert.Equal("Handle", kernel.LastExecution!.Entrypoint);

        // Retained history must be bounded rather than scaling with event volume.
        Assert.True(
            retained <= RetentionCeiling,
            $"Expected bounded retention (<= {RetentionCeiling}) but the observer kept {retained} observations.");
    }

    [Fact]
    public async Task ExecutionObservations_count_does_not_scale_with_event_count()
    {
        var smallCount = await RetainedObservationCountAsync(eventCount: 50);
        var largeCount = await RetainedObservationCountAsync(eventCount: 250);

        // Under a bounded window the retained count saturates and stops tracking
        // event volume. With unbounded retention it grows 1:1 with executions, so
        // processing 5x the events keeps roughly 5x the observations.
        Assert.True(
            largeCount <= RetentionCeiling,
            $"Expected bounded retention (<= {RetentionCeiling}) but kept {largeCount} observations for 250 events.");
        Assert.True(
            largeCount < smallCount * 4,
            $"Retained observation count scaled with event volume: {smallCount} for 50 events vs {largeCount} for 250 events.");
    }

    private static async Task<int> RetainedObservationCountAsync(int eventCount)
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(
            messages,
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        for (var i = 0; i < eventCount; i++)
        {
            await server.Hooks.PublishAsync(new DamageEvent("fire", 120, $"player-{i}"));
        }

        return kernel.ExecutionObservations.Count;
    }
}
