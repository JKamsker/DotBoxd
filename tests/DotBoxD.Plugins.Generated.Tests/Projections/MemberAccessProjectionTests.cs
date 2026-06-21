namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted projections that read through a nested member (a <c>record.get</c> chain on the event)
/// and chained <c>Select</c> stages with no intervening <c>Where</c>. The projected leaf value (a nested record's
/// field) round-trips to the native <c>RunLocal</c> delegate.
/// </summary>
public sealed class MemberAccessProjectionTests
{
    [Fact]
    public async Task Nested_member_access_projection_reads_the_inner_field()
    {
        // Select(e => e.Player.Name): record.get(Player) then record.get(Name) -> string, server-side.
        var received = new List<string>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Player.Name)
            .RunLocal((name, ctx) => received.Add(name));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal("hero", Assert.Single(received));
    }

    [Fact]
    public async Task Chained_select_projects_to_an_inner_string()
    {
        // Select(e => e.Player).Select(p => p.Name): two projection stages with no Where between them.
        var received = new List<string>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Player)
            .Select(p => p.Name)
            .RunLocal((name, ctx) => received.Add(name));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal("hero", Assert.Single(received));
    }

    [Fact]
    public async Task Chained_select_projects_to_an_inner_scalar()
    {
        // Select(e => e.Player).Select(p => p.Level): chained projection ending in an int scalar.
        var received = new List<int>();
        using var h = new RunLocalHarness<EncounterEvent>();

        h.Hooks.On<EncounterEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Player)
            .Select(p => p.Level)
            .RunLocal((level, ctx) => received.Add(level));

        await h.PublishAsync(SampleEvents.Matching);
        await h.PublishAsync(SampleEvents.Filtered);

        Assert.Equal(7, Assert.Single(received));
    }
}
