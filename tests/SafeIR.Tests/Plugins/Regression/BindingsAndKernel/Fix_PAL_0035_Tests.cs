using SafeIR;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0035: plugin hook dispatch snapshots the filter and
/// handler lists into brand-new arrays on every event publish. Pipelines are stable
/// after setup, yet <c>HookPipeline&lt;TEvent&gt;.PublishAsync</c> still calls
/// <c>_filters.ToArray()</c> plus <c>_handlers.ToArray()</c> under the lock for each
/// published event, allocating two reference arrays whose size scales with the number
/// of registered filters/handlers.
///
/// The correct post-fix behavior (copy-on-write cached arrays read by reference) makes
/// steady-state per-publish allocation independent of pipeline size: a large pipeline
/// must not allocate meaningfully more per publish than a tiny one. These tests measure
/// allocated bytes on the publishing thread and assert that size-independence, so they
/// are red while the per-publish <c>ToArray()</c> snapshots remain.
/// </summary>
public sealed class Fix_PAL_0035_Tests
{
    private sealed record PingEvent(int Value);

    private sealed class PingEventAdapter : IPluginEventAdapter<PingEvent>
    {
        public string EventName => "PingEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_Value", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(PingEvent e)
            => [SandboxValue.FromInt32(e.Value)];
    }

    // Iterations to reach allocation steady state; large enough to average out JIT/first-call noise.
    private const int PublishIterations = 20_000;

    // A pipeline this wide makes the two per-publish snapshot arrays dominate any constant
    // per-publish overhead (such as the HookContext allocation) under the current code.
    private const int WidePipelineSize = 64;

    [Fact]
    public async Task SteadyStatePublish_doesNotAllocatePerPublishHandlerSnapshots()
    {
        // A wide, stable pipeline: many no-op filters that pass plus many no-op handlers.
        var server = PluginServer.Create();
        var pipeline = server.Hooks.On(new PingEventAdapter());
        for (var i = 0; i < WidePipelineSize; i++)
        {
            pipeline.Where((_, _) => true);
            pipeline.InvokeHostHandler((PingEvent _, HookContext _) => { });
        }

        var perPublishWide = await MeasureBytesPerPublishAsync(server);

        // Per-publish snapshot of WidePipelineSize filters + WidePipelineSize handlers on a
        // 64-bit runtime costs at least this many bytes (two reference arrays). The fixed
        // copy-on-write design pays none of it on the steady-state publish path.
        const long snapshotFloorBytes = WidePipelineSize * 8L;

        Assert.True(
            perPublishWide < snapshotFloorBytes,
            $"Steady-state publish allocated {perPublishWide} bytes/publish for a {WidePipelineSize}-filter/" +
            $"{WidePipelineSize}-handler pipeline; the per-publish handler/filter snapshot arrays " +
            $"(>= {snapshotFloorBytes} bytes) are still being allocated.");
    }

    [Fact]
    public async Task PerPublishAllocation_doesNotScaleWithPipelineSize()
    {
        var tinyServer = PluginServer.Create();
        var tinyPipeline = tinyServer.Hooks.On(new PingEventAdapter());
        tinyPipeline.InvokeHostHandler((PingEvent _, HookContext _) => { });

        var wideServer = PluginServer.Create();
        var widePipeline = wideServer.Hooks.On(new PingEventAdapter());
        for (var i = 0; i < WidePipelineSize; i++)
        {
            widePipeline.Where((_, _) => true);
            widePipeline.InvokeHostHandler((PingEvent _, HookContext _) => { });
        }

        var perPublishTiny = await MeasureBytesPerPublishAsync(tinyServer);
        var perPublishWide = await MeasureBytesPerPublishAsync(wideServer);

        // With per-publish ToArray() snapshots, the wide pipeline allocates two arrays sized
        // by WidePipelineSize on every publish, so its per-publish cost is far above the tiny
        // pipeline's. The fixed copy-on-write design keeps per-publish allocation independent
        // of pipeline size, so the wide cost must not exceed the tiny cost by the snapshot bulk.
        const long snapshotGrowthFloorBytes = WidePipelineSize * 8L;

        Assert.True(
            perPublishWide - perPublishTiny < snapshotGrowthFloorBytes,
            $"Per-publish allocation scaled with pipeline size: tiny={perPublishTiny} bytes, " +
            $"wide={perPublishWide} bytes (delta {perPublishWide - perPublishTiny} >= " +
            $"{snapshotGrowthFloorBytes}). Handler/filter snapshots are allocated per publish.");
    }

    private static async Task<long> MeasureBytesPerPublishAsync(PluginServer server)
    {
        var e = new PingEvent(7);

        // Warm up so first-call JIT and one-time allocations are excluded from the measurement.
        for (var i = 0; i < 1_000; i++)
        {
            await server.Hooks.PublishAsync(e);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < PublishIterations; i++)
        {
            await server.Hooks.PublishAsync(e);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / PublishIterations;
    }
}
