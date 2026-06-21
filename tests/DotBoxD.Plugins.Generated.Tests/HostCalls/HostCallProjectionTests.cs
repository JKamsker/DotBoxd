namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted host-call projections: a server-side <c>Select((e, ctx) =&gt; ctx.Host&lt;T&gt;()...)</c> whose
/// host-binding RESULT becomes the projected value pushed to the native <c>RunLocal</c>. Covers a scalar host read,
/// a Guid-returning (allocating) host call, and the marquee shape — a host read returning a list whose <c>.Count</c>
/// is filtered downstream, all server-side. The bindings run under <see cref="HostBindingSupport"/>'s probe host.
/// </summary>
public sealed class HostCallProjectionTests
{
    [Fact]
    public async Task Scalar_host_read_in_a_select_projects_to_run_local()
    {
        var received = new List<int>();
        using var h = new RunLocalHarness<EncounterEvent>(
            HostBindingSupport.ProbeReadPolicy(), HostBindingSupport.AddBindings);

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select((e, ctx) => ctx.Host<IScalarWorld>().GetValue(e.Zone))
            .RunLocal((value, ctx) => received.Add(value));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(HostBindingSupport.ScalarValue, Assert.Single(received));
    }

    [Fact]
    public async Task Guid_returning_host_call_in_a_select_round_trips()
    {
        var received = new List<Guid>();
        using var h = new RunLocalHarness<EncounterEvent>(
            HostBindingSupport.ProbeReadPolicy(), HostBindingSupport.AddBindings);

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select((e, ctx) => ctx.Host<IIdWorld>().GenerateId(e.Zone))
            .RunLocal((id, ctx) => received.Add(id));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(SampleEvents.SampleId, Assert.Single(received));
    }

    [Fact]
    public async Task Host_list_read_with_a_downstream_count_filter_runs_server_side()
    {
        // Select((e, ctx) => ctx.Host<ITagWorld>().GetTags(e.Zone)) returns a list from a host binding, and
        // .Where(tags => tags.Count > 1) reads its size via list.count — all server-side. "crypt" yields 3 tags
        // (Count > 1 -> pushed); "void" yields 1 tag (filtered downstream before any push).
        var received = new List<IReadOnlyList<string>>();
        using var h = new RunLocalHarness<EncounterEvent>(
            HostBindingSupport.ProbeReadPolicy(), HostBindingSupport.AddBindings);

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select((e, ctx) => ctx.Host<ITagWorld>().GetTags(e.Zone))
            .Where(tags => tags.Count > 1)
            .RunLocal((tags, ctx) => received.Add(tags));

        await h.PublishAsync(SampleEvents.Matching);                          // "crypt" -> 3 tags -> matches
        await h.PublishAsync(SampleEvents.Matching with { Zone = "void" });   // "void"  -> 1 tag  -> filtered

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, Assert.Single(received));
    }
}
