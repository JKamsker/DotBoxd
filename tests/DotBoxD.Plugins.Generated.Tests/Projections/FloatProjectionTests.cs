namespace DotBoxD.Plugins.Generated.Tests;

/// <summary>
/// Build-time-intercepted direct <c>float</c> scalar projection: the projected float widens to the sandbox F64 wire
/// kind and narrows back exactly on the client (the literal is exactly representable, so the round-trip is lossless).
/// </summary>
public sealed class FloatProjectionTests
{
    [Fact]
    public async Task Float_projection_round_trips_losslessly()
    {
        var received = new List<float>();
        using var h = new RunLocalHarness<FloatEvent>();

        h.Hooks.On<FloatEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Health)
            .RunLocal((health, ctx) => received.Add(health));

        await h.PublishAsync(new FloatEvent(3, 0.5f));
        await h.PublishAsync(new FloatEvent(99, 0.5f));

        Assert.Equal(0.5f, Assert.Single(received));
    }
}
